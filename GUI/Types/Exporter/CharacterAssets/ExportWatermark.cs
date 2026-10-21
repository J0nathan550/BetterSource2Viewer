using System.Globalization;
using System.IO;

namespace GUI.Types.Exporter.CharacterAssets
{
    /// <summary>
    /// The text every exported hero shows in the loadout screen, a sprite particle its loadout sequences create, see
    /// <see cref="ModelDocEditor.AddLoadoutParticle"/>.
    /// </summary>
    static class ExportWatermark
    {
        public const string Text = "t.me/j0nvpk";

        /// <summary>The particle the sequences that play once create, which lasts until another sequence starts.</summary>
        public const string Particle = $"{ParticleFixes.CopiesFolder}/watermark.vpcf";

        /// <summary>The particle's control point configuration that places it on the hero.</summary>
        public const string Config = "loadout";

        private const string Texture = "materials/j0nathan550/watermark";

        /// <summary>
        /// The particle a looping sequence creates, which ends as the loop does, just as the next loop creates it again.
        /// </summary>
        /// <param name="loopMilliseconds">How long one loop of the sequence plays.</param>
        public static string GetLoopParticle(int loopMilliseconds)
            => string.Create(CultureInfo.InvariantCulture, $"{ParticleFixes.CopiesFolder}/watermark_{loopMilliseconds}ms.vpcf");

        /// <summary>
        /// Writes the particles and their texture into the addon's content folder.
        /// </summary>
        /// <param name="loopMilliseconds">The loop lengths <see cref="GetLoopParticle"/> was asked for.</param>
        public static void Write(string contentRoot, IEnumerable<int> loopMilliseconds)
        {
            Directory.CreateDirectory(Path.Combine(contentRoot, Path.GetDirectoryName(Texture)!));
            Directory.CreateDirectory(Path.Combine(contentRoot, Path.GetDirectoryName(Particle)!));

            // Drawn ahead of time, white with a dark outline so it reads over any background, since the machine
            // exporting may have no font to draw it with
            using var image = typeof(ExportWatermark).Assembly.GetManifestResourceStream("CharacterAssets.watermark.png")
                ?? throw new InvalidDataException("The watermark image is missing from the build");
            using var file = File.Create(Path.Combine(contentRoot, Texture + ".png"));
            image.CopyTo(file);

            File.WriteAllText(Path.Combine(contentRoot, Texture + ".vtex"), VtexText);
            File.WriteAllText(Path.Combine(contentRoot, Particle), GetVpcfText(lifetimeMilliseconds: null));

            foreach (var milliseconds in loopMilliseconds)
            {
                File.WriteAllText(Path.Combine(contentRoot, GetLoopParticle(milliseconds)), GetVpcfText(milliseconds));
            }
        }

        private const string VtexText = $$"""
            <!-- dmx encoding keyvalues2_noids 1 format vtex 1 -->
            "CDmeVtex"
            {
            	"m_inputTextureArray" "element_array"
            	[
            		"CDmeInputTexture"
            		{
            			"m_name" "string" "InputTexture0"
            			"m_fileName" "string" "{{Texture}}.png"
            			"m_colorSpace" "string" "srgb"
            			"m_typeString" "string" "2D"
            			"m_imageProcessorArray" "element_array"
            			[
            				"CDmeImageProcessor"
            				{
            					"m_algorithm" "string" "None"
            					"m_stringArg" "string" ""
            					"m_vFloat4Arg" "vector4" "0 0 0 0"
            				}
            			]
            		}
            	]
            	"m_outputTypeString" "string" "2D"
            	"m_outputFormat" "string" "DXT5"
            	"m_outputClearColor" "vector4" "0 0 0 0"
            	"m_nOutputMinDimension" "int" "0"
            	"m_nOutputMaxDimension" "int" "0"
            	"m_textureOutputChannelArray" "element_array"
            	[
            		"CDmeTextureOutputChannel"
            		{
            			"m_inputTextureArray" "string_array"
            			[
            				"InputTexture0"
            			]
            			"m_srcChannels" "string" "rgba"
            			"m_dstChannels" "string" "rgba"
            			"m_mipAlgorithm" "CDmeImageProcessor"
            			{
            				"m_algorithm" "string" "Box"
            				"m_stringArg" "string" ""
            				"m_vFloat4Arg" "vector4" "0 0 0 0"
            			}

            			"m_outputColorSpace" "string" "srgb"
            		}
            	]
            	"m_vClamp" "vector3" "0 0 0"
            	"m_bNoLod" "bool" "0"
            }

            """;

        // One sprite at the hero's feet, in front of it, that follows the hero. Without a lifetime it never dies.
        private static string GetVpcfText(int? lifetimeMilliseconds)
        {
            var decay = lifetimeMilliseconds == null
                ? string.Empty
                : """
                    	m_Operators =
                    	[
                    		{
                    			_class = "C_OP_Decay"
                    		},
                    	]

                    """;
            var lifetime = lifetimeMilliseconds is { } milliseconds
                ? string.Create(CultureInfo.InvariantCulture, $$"""
                    		{
                    			_class = "C_INIT_InitFloat"
                    			m_InputValue =
                    			{
                    				m_nType = "PF_TYPE_LITERAL"
                    				m_flLiteralValue = {{milliseconds / 1000f:0.0##}}
                    			}
                    			m_nOutputField = 1
                    		},

                    """)
                : string.Empty;

            return $$"""
            <!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:vpcf45:version{73c3d623-a141-4df2-b548-41dd786e6300} -->
            {
            	_class = "CParticleSystemDefinition"
            	m_nMaxParticles = 1
            	m_flConstantRadius = 60.0
            	m_ConstantColor = [ 255, 255, 255, 255 ]
            	m_Renderers =
            	[
            		{
            			_class = "C_OP_RenderSprites"
            			m_bDisableZBuffering = true
            			m_vecTexturesInput =
            			[
            				{
            					m_hTexture = resource:"{{Texture}}.vtex"
            				},
            			]
            		},
            	]
            {{decay}}	m_Initializers =
            	[
            {{lifetime}}            		{
            			_class = "C_INIT_CreateWithinSphere"
            			m_fRadiusMin = 0.0
            			m_fRadiusMax = 0.0
            		},
            		{
            			_class = "C_INIT_PositionOffset"
            			m_OffsetMin = [ 60.0, 0.0, 20.0 ]
            			m_OffsetMax = [ 60.0, 0.0, 20.0 ]
            			m_bLocalCoords = true
            		},
            	]
            	m_Emitters =
            	[
            		{
            			_class = "C_OP_InstantaneousEmitter"
            			m_nParticlesToEmit =
            			{
            				m_nType = "PF_TYPE_LITERAL"
            				m_flLiteralValue = 1.0
            			}
            		},
            	]
            	m_controlPointConfigurations =
            	[
            		{
            			m_name = "{{Config}}"
            			m_drivers =
            			[
            				{
            					m_iAttachType = "PATTACH_ABSORIGIN_FOLLOW"
            					m_entityName = "self"
            				},
            			]
            		},
            	]
            	m_nBehaviorVersion = 12
            }

            """;
        }
    }
}
