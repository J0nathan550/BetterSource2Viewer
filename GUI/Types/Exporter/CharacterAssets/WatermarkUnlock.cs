// Character export feature of BetterSource2Viewer.
// Copyright (c) 2026 J0nathan550 (https://t.me/j0nvpk).
// Who may leave the watermark out of exports is governed by LICENSE.md in this directory.

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using GUI.Utils;

namespace GUI.Types.Exporter.CharacterAssets
{
    /// <summary>
    /// Lets the Discord accounts listed in <see cref="WhitelistUrl"/> leave <see cref="ExportWatermark"/> out of their
    /// exports. The account is signed in from the settings through Discord's OAuth2 with PKCE, redirecting to a local
    /// listener, so no client secret or server of our own is needed.
    /// </summary>
    /// <remarks>
    /// Only the tokens and the wish to remove the watermark are saved. Who the account is and whether it is
    /// whitelisted are asked from Discord and the whitelist again each run, so editing the saved file unlocks nothing.
    /// </remarks>
    static class WatermarkUnlock
    {
        /// <summary>
        /// The Discord application's client ID. In the Developer Portal its OAuth2 page must have Public Client turned
        /// on and <see cref="RedirectUri"/> added as a redirect. Empty hides signing in.
        /// </summary>
        private const string ClientId = "1563162212540092466";

        private const int RedirectPort = 53682;
        private const string CallbackPath = "/discord/callback";
        private const string WhitelistUrl = "https://raw.githubusercontent.com/J0nathan550/BetterSource2Viewer/master/watermark-whitelist.txt";
        private const string DiscordApi = "https://discord.com/api/v10";

        private static readonly string RedirectUri = string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{RedirectPort}{CallbackPath}");

        private static Session? CurrentSession;
        private static Task? Verification;

        /// <summary>Whether signing in is offered at all.</summary>
        public static bool IsAvailable => ClientId.Length > 0;

        /// <summary>Whether tokens from an earlier sign in are saved, which <see cref="VerifyAsync"/> checks.</summary>
        public static bool HasSession => LoadSession() != null;

        /// <summary>The signed in account's display name, once <see cref="VerifyAsync"/> confirmed it.</summary>
        public static string? UserName { get; private set; }

        /// <summary>Whether the signed in account is on the whitelist, once <see cref="VerifyAsync"/> confirmed it.</summary>
        public static bool IsWhitelisted { get; private set; }

        /// <summary>Whether exports leave the watermark out: only when the whitelisted account asked for it.</summary>
        public static bool RemoveWatermark
        {
            get => IsWhitelisted && CurrentSession?.RemoveWatermark == true;
            set
            {
                if (!IsWhitelisted || CurrentSession == null)
                {
                    return;
                }

                CurrentSession.RemoveWatermark = value;
                SaveSession(CurrentSession);
            }
        }

        private static string SessionPath => Path.Combine(Settings.SettingsFolder, "discord.json");

        /// <summary>
        /// Confirms the saved account with Discord and reads the whitelist, once per run. Never throws: an account
        /// that could not be confirmed, e.g. when offline, keeps the watermark.
        /// </summary>
        public static Task VerifyAsync() => Verification ??= VerifyCoreAsync();

        /// <summary>
        /// Opens Discord's authorization page in the browser and waits for it to redirect back, then verifies the account.
        /// </summary>
        /// <exception cref="OperationCanceledException">The wait was cancelled.</exception>
        /// <exception cref="InvalidOperationException">Discord refused, or answered with something unexpected.</exception>
        public static async Task SignInAsync(CancellationToken cancellationToken)
        {
            var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
            var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            var state = Base64Url(RandomNumberGenerator.GetBytes(16));

            using var listener = new HttpListener();
            listener.Prefixes.Add(string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{RedirectPort}/"));
            listener.Start();

            OpenBrowser($"https://discord.com/oauth2/authorize?response_type=code&scope=identify&client_id={ClientId}" +
                $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}&state={state}&code_challenge={challenge}&code_challenge_method=S256");

            var code = await WaitForCodeAsync(listener, state, cancellationToken).ConfigureAwait(false);

            using var http = CreateHttpClient();
            var token = await RequestTokenAsync(http, new()
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = RedirectUri,
                ["client_id"] = ClientId,
                ["code_verifier"] = verifier,
            }, cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("Discord did not accept the sign in.");

            SaveSession(token);
            Verification = VerifyCoreAsync();
            await Verification.ConfigureAwait(false);
        }

        /// <summary>Forgets the account, which brings the watermark back.</summary>
        public static void SignOut()
        {
            CurrentSession = null;
            UserName = null;
            IsWhitelisted = false;
            Verification = Task.CompletedTask;

            try
            {
                File.Delete(SessionPath);
            }
            catch (IOException e)
            {
                Log.Warn(nameof(WatermarkUnlock), $"Failed to delete the Discord session: {e.Message}");
            }
        }

        private static async Task VerifyCoreAsync()
        {
            CurrentSession = null;
            UserName = null;
            IsWhitelisted = false;

            var session = LoadSession();

            if (session == null)
            {
                return;
            }

            try
            {
                using var http = CreateHttpClient();
                var user = DateTimeOffset.UtcNow.ToUnixTimeSeconds() < session.ExpiresAt
                    ? await GetUserAsync(http, session.AccessToken).ConfigureAwait(false)
                    : null;

                if (user == null)
                {
                    var refreshed = await RequestTokenAsync(http, new()
                    {
                        ["grant_type"] = "refresh_token",
                        ["refresh_token"] = session.RefreshToken,
                        ["client_id"] = ClientId,
                    }, CancellationToken.None).ConfigureAwait(false);

                    if (refreshed == null)
                    {
                        Log.Info(nameof(WatermarkUnlock), "The Discord sign in expired, sign in again from the settings");
                        SignOut();
                        return;
                    }

                    refreshed.RemoveWatermark = session.RemoveWatermark;
                    session = refreshed;
                    SaveSession(session);
                    user = await GetUserAsync(http, session.AccessToken).ConfigureAwait(false)
                        ?? throw new InvalidOperationException("Discord did not return the account.");
                }

                CurrentSession = session;
                UserName = user.GlobalName ?? user.UserName;

                string whitelist;

                try
                {
                    whitelist = await http.GetStringAsync(new Uri(WhitelistUrl)).ConfigureAwait(false);
                }
                catch (HttpRequestException e)
                {
                    Log.Warn(nameof(WatermarkUnlock), $"Failed to read the watermark whitelist from {WhitelistUrl}: {e.Message}");
                    return;
                }

                IsWhitelisted = ParseWhitelist(whitelist).Contains(user.Id);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
            {
                Log.Warn(nameof(WatermarkUnlock), $"Failed to verify the Discord account: {e.Message}");
            }
        }

        /// <summary>Discord user IDs, one per line, with anything after a '#' ignored.</summary>
        private static HashSet<string> ParseWhitelist(string text) => text
            .Split('\n')
            .Select(static line => (line.IndexOf('#', StringComparison.Ordinal) is var comment and >= 0 ? line[..comment] : line).Trim())
            .Where(static id => id.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        private static async Task<string> WaitForCodeAsync(HttpListener listener, string state, CancellationToken cancellationToken)
        {
            while (true)
            {
                var context = await listener.GetContextAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
                var request = context.Request;

                // Browsers also ask for things like the favicon
                if (request.Url?.AbsolutePath != CallbackPath)
                {
                    context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                    context.Response.Close();
                    continue;
                }

                var code = request.QueryString["code"];
                var accepted = request.QueryString["state"] == state && !string.IsNullOrEmpty(code);

                await RespondAsync(context.Response, accepted
                    ? "Signed in to BetterSource2Viewer. You can close this tab."
                    : "Signing in to BetterSource2Viewer failed. You can close this tab and try again.").ConfigureAwait(false);

                if (!accepted)
                {
                    throw new InvalidOperationException(request.QueryString["error_description"] ?? "Discord did not authorize the sign in.");
                }

                return code!;
            }
        }

        private static async Task RespondAsync(HttpListenerResponse response, string message)
        {
            var body = Encoding.UTF8.GetBytes($"<!doctype html><meta charset=\"utf-8\"><title>BetterSource2Viewer</title><p style=\"font-family:sans-serif\">{message}</p>");
            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = body.Length;
            await response.OutputStream.WriteAsync(body).ConfigureAwait(false);
            response.Close();
        }

        /// <returns>The new session, or null when Discord refused the grant.</returns>
        private static async Task<Session?> RequestTokenAsync(HttpClient http, Dictionary<string, string> form, CancellationToken cancellationToken)
        {
            using var content = new FormUrlEncodedContent(form);
            using var response = await http.PostAsync(new Uri($"{DiscordApi}/oauth2/token"), content, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var token = await JsonSerializer.DeserializeAsync(stream, WatermarkUnlockJsonContext.Default.TokenResponse, cancellationToken).ConfigureAwait(false);

            if (token is not { AccessToken.Length: > 0, RefreshToken.Length: > 0 })
            {
                throw new InvalidOperationException("Discord returned no token.");
            }

            return new Session
            {
                AccessToken = token.AccessToken,
                RefreshToken = token.RefreshToken,
                ExpiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + token.ExpiresIn,
            };
        }

        /// <returns>The account, or null when the token is no longer valid.</returns>
        private static async Task<DiscordUser?> GetUserAsync(HttpClient http, string accessToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"{DiscordApi}/users/@me"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await http.SendAsync(request).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            var user = await JsonSerializer.DeserializeAsync(stream, WatermarkUnlockJsonContext.Default.DiscordUser).ConfigureAwait(false);

            return user is { Id.Length: > 0 } ? user : throw new InvalidOperationException("Discord returned no account.");
        }

        private static HttpClient CreateHttpClient()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.Add("User-Agent", $"Source2Viewer/{Program.ProductVersion} (+https://github.com/J0nathan550/BetterSource2Viewer)");
            return http;
        }

        private static void OpenBrowser(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Win32Exception e)
            {
                throw new InvalidOperationException($"Failed to open the browser: {e.Message}", e);
            }
        }

        private static string Base64Url(byte[] bytes)
            => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static Session? LoadSession()
        {
            try
            {
                using var stream = File.OpenRead(SessionPath);
                return JsonSerializer.Deserialize(stream, WatermarkUnlockJsonContext.Default.Session);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
            {
                return null;
            }
        }

        private static void SaveSession(Session session)
        {
            try
            {
                using var stream = File.Create(SessionPath);
                JsonSerializer.Serialize(stream, session, WatermarkUnlockJsonContext.Default.Session);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Warn(nameof(WatermarkUnlock), $"Failed to save the Discord session: {e.Message}");
            }
        }

        internal sealed class Session
        {
            public string AccessToken { get; set; } = string.Empty;
            public string RefreshToken { get; set; } = string.Empty;
            public long ExpiresAt { get; set; }
            public bool RemoveWatermark { get; set; }
        }

        internal sealed class TokenResponse
        {
            [JsonPropertyName("access_token")]
            public string AccessToken { get; set; } = string.Empty;

            [JsonPropertyName("refresh_token")]
            public string RefreshToken { get; set; } = string.Empty;

            [JsonPropertyName("expires_in")]
            public long ExpiresIn { get; set; }
        }

        internal sealed class DiscordUser
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("username")]
            public string UserName { get; set; } = string.Empty;

            [JsonPropertyName("global_name")]
            public string? GlobalName { get; set; }
        }
    }

    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
    [JsonSerializable(typeof(WatermarkUnlock.Session))]
    [JsonSerializable(typeof(WatermarkUnlock.TokenResponse))]
    [JsonSerializable(typeof(WatermarkUnlock.DiscordUser))]
    partial class WatermarkUnlockJsonContext : JsonSerializerContext
    {
    }
}
