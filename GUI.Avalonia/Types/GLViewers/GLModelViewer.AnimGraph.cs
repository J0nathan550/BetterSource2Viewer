using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Layout;
using Avalonia.Threading;
using GUI.Controls;
using GUI.Utils;
using ValveResourceFormat.Animation;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;
using AvaloniaControls = Avalonia.Controls;

namespace GUI.Types.GLViewers
{
    // The Avalonia counterpart of GUI/Types/GLViewers/GLModelViewer.AnimGraph.cs, which builds its controls with WinForms.
    // Only the parts that create or update controls differ, keep the rest in sync with it.
    partial class GLModelViewer
    {
        // Time scale, pause and single step under the parameters of whichever graph plays
        private void AddGraphPlaybackControls()
        {
            Debug.Assert(UiControl != null);
            Debug.Assert(animationController != null);

            var controller = animationController;

            graphTimeScaleSlider = new Slider
            {
                Value = controller.FrametimeMultiplier,
                ValueChanged = timeScale => controller.FrametimeMultiplier = timeScale,
            };

            // The buttons are labelled instead of showing icons
            graphPlayImage = graphPauseImage = graphTickImage = null;

            graphPauseButton = new ThemedButton("Pause", () => SetGraphPaused(!controller.IsPaused));

            var tickButton = new ThemedButton("Step", () =>
            {
                SetGraphPaused(true);
                controller.StepGraph(GraphTickTime);
            }, repeatWhileHeld: true);

            graphEventList = new EventListControl(GraphEventListLines)
            {
                EmptyText = "No events",
            };

            var buttons = new AvaloniaControls.StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children = { graphPauseButton.Native, tickButton.Native },
            };

            var panel = new System.Windows.Forms.Panel
            {
                Visible = false,
            };

            AddRows(panel,
            [
                CreateSectionHeader("System"),
                CreatePropertyRow("Time scale", graphTimeScaleSlider.Native),
                buttons,
                CreateSectionHeader("Events"),
                graphEventList.Native,
            ]);

            graphPlaybackControls = panel;
            UiControl.AddControl(panel);
        }

        private void SetGraphPaused(bool paused)
        {
            Debug.Assert(animationController != null);

            animationController.IsPaused = paused;
            graphPauseButton?.Text = paused ? "Play" : "Pause";
        }

        /// <summary>One of the model's animation graphs, with the parameter controls built for it.</summary>
        private sealed class GraphSession(string displayName, AnimationGraph graph)
        {
            public string DisplayName { get; } = displayName;
            public AnimationGraph Graph { get; } = graph;
            public AvaloniaControls.StackPanel? Controls { get; set; }
            public List<GraphGizmoBinding> GizmoBindings { get; } = [];
        }

        private GraphSession? LoadGraphSession(string identifier, string path)
        {
            Debug.Assert(modelSceneNode != null);

            if (modelSceneNode.LoadAnimationGraph(path) is not { } graph)
            {
                Log.Warn(nameof(GLModelViewer), $"Failed to load animation graph {path}");
                return null;
            }

            ApplyStartingParameters(graph);

            var displayName = string.IsNullOrEmpty(identifier) ? Path.GetFileNameWithoutExtension(path) : identifier;
            return new GraphSession(displayName, graph);
        }

        private void BuildGraphControls(GraphSession session)
        {
            Debug.Assert(graphControlsHost != null);

            var controls = CreateAnimGraphControls(session.Graph, session.GizmoBindings);
            controls.IsVisible = false;

            ((AvaloniaControls.Panel)graphControlsHost.Native).Children.Add(controls);
            session.Controls = controls;
        }

        private void ShowGraphControls(GraphSession? session)
        {
            Debug.Assert(UiControl != null);

            var showGraph = session?.Controls != null;

            foreach (var other in graphSessions)
            {
                other.Controls?.IsVisible = other == session;
            }

            graphControlsHost?.Height = showGraph ? 1 : 0;

            // Playback controls only apply to clips
            animationTimeLabel?.Visible = !showGraph;
            animationTrackBar?.Visible = !showGraph;
            SetRowVisible(animationPlayPause, !showGraph);
            slowmodeTrackBar?.Visible = !showGraph;
            SetRowVisible(rootMotionCheckBox, !showGraph);
            SetRowVisible(additiveCheckBox, !showGraph);

            graphPlaybackControls?.Visible = showGraph;

            // Clips and graphs have their own pause and speed controls, and neither inherits those of the other
            if ((showGraph || graphControlsShown) && animationController != null)
            {
                animationPlayPause?.Checked = true;
                SetGraphPaused(false);
                animationController.FrametimeMultiplier = 1f;
                slowmodeTrackBar?.Slider.Value = 1f;
                graphTimeScaleSlider?.Value = 1f;
            }

            graphControlsShown = showGraph;
        }

        // Sidebar check boxes sit in a row control of their own, which is what has to be hidden
        private static void SetRowVisible(System.Windows.Forms.Control? control, bool visible)
        {
            if (control == null)
            {
                return;
            }

            var row = control.Native.Parent as AvaloniaControls.Control ?? control.Native;
            row.IsVisible = visible;
        }

        private static void AddRows(System.Windows.Forms.Panel panel, AvaloniaControls.Control[] rows)
        {
            var children = ((AvaloniaControls.Panel)panel.Native).Children;

            foreach (var row in rows)
            {
                children.Add(row);
            }
        }

        private static AvaloniaControls.TextBlock CreateSectionHeader(string text)
        {
            var header = new AvaloniaControls.TextBlock { Text = text };
            header.Classes.Add("groupHeader");
            return header;
        }

        private static AvaloniaControls.Grid CreatePropertyRow(string name, AvaloniaControls.Control? editor, params AvaloniaControls.Control[] trailing)
        {
            var label = new AvaloniaControls.TextBlock
            {
                Text = name,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                Margin = new(0, 0, 6, 0),
            };

            AvaloniaControls.ToolTip.SetTip(label, name);

            var row = new AvaloniaControls.Grid
            {
                ColumnDefinitions = new AvaloniaControls.ColumnDefinitions($"*,{string.Join(',', Enumerable.Repeat("Auto", trailing.Length + 1))}"),
                Margin = new(0, 1),
            };

            row.Children.Add(label);

            var column = 1;

            foreach (var control in trailing.Prepend(editor).OfType<AvaloniaControls.Control>())
            {
                control.Margin = new(2, 0, 0, 0);
                AvaloniaControls.Grid.SetColumn(control, column++);
                row.Children.Add(control);
            }

            return row;
        }

        private static AvaloniaControls.NumericUpDown CreateFloatField(float value, int decimals, Action<float> changeCallback)
        {
            var field = new AvaloniaControls.NumericUpDown
            {
                Value = (decimal)value,
                Increment = 0.1m,
                FormatString = $"F{decimals}",
                Width = 90,
                Minimum = decimal.MinValue,
                Maximum = decimal.MaxValue,
            };

            field.ValueChanged += (_, e) =>
            {
                if (e.NewValue is { } newValue)
                {
                    changeCallback((float)newValue);
                }
            };

            return field;
        }

        private static AvaloniaControls.ComboBox CreateComboBox(IEnumerable<string> items, string? selected, Action<string> changeCallback)
        {
            var options = items.ToList();

            var comboBox = new AvaloniaControls.ComboBox
            {
                ItemsSource = options,
                SelectedItem = selected != null && options.Contains(selected) ? selected : null,
                MinWidth = 90,
            };

            comboBox.SelectionChanged += (_, _) =>
            {
                if (comboBox.SelectedItem is string option)
                {
                    changeCallback(option);
                }
            };

            return comboBox;
        }

        private static AvaloniaControls.CheckBox CreateCheckBox(bool isChecked, Action<bool> changeCallback, string text = "")
        {
            var checkBox = new AvaloniaControls.CheckBox
            {
                IsChecked = isChecked,
                Content = text,
            };

            checkBox.IsCheckedChanged += (_, _) => changeCallback(checkBox.IsChecked == true);
            return checkBox;
        }

        private static AvaloniaControls.Button CreateButton(string text, Action onClick)
        {
            var button = new AvaloniaControls.Button { Content = text };
            button.Click += (_, _) => onClick();
            return button;
        }

        private const string UnsetIdText = "(none)";

        private static (AvaloniaControls.Control Editor, AvaloniaControls.Control[] Trailing) CreateFloatParameterEditor(AnimationGraph graph, string paramName)
        {
            var value = graph.FloatParameters[paramName];
            void SetValue(float newValue) => graph.FloatParameters[paramName] = newValue;

            var range = graph.GetFloatParameterRange(paramName) ?? GuessFloatParameterRange(paramName);

            if (range is not { } knownRange)
            {
                return (CreateFloatField(value, 3, SetValue), []);
            }

            if (knownRange.IsDiscrete)
            {
                var options = Enumerable.Range((int)knownRange.Min, (int)(knownRange.Max - knownRange.Min) + 1)
                    .Select(static option => option.ToString(CultureInfo.InvariantCulture));

                return (CreateComboBox(options, ((int)value).ToString(CultureInfo.InvariantCulture),
                    option => SetValue(float.Parse(option, CultureInfo.InvariantCulture))), []);
            }

            var field = CreateFloatField(value, knownRange.IsWholeNumber ? 0 : 3, SetValue);
            field.Minimum = (decimal)knownRange.Min;
            field.Maximum = (decimal)knownRange.Max;
            field.Increment = knownRange.IsWholeNumber ? 1m : (decimal)((knownRange.Max - knownRange.Min) / 100f);

            var slider = new AvaloniaControls.Slider
            {
                Minimum = knownRange.Min,
                Maximum = knownRange.Max,
                Value = value,
                IsSnapToTickEnabled = knownRange.IsWholeNumber,
                TickFrequency = 1,
                Width = 80,
            };

            // The slider and the field show the same value, each moves the other
            slider.ValueChanged += (_, e) => field.Value = (decimal)e.NewValue;
            field.ValueChanged += (_, e) =>
            {
                if (e.NewValue is { } newValue)
                {
                    slider.Value = (double)newValue;
                }
            };

            return (slider, [field]);
        }

        // Starts the unset IDs that are always set at runtime, so a picked graph shows a standing character rather than
        // nothing: an idle state or action, ground movement, and a direction
        private static void ApplyStartingParameters(AnimationGraph graph)
        {
            foreach (var paramName in graph.IdParameters.Keys.ToArray())
            {
                if (graph.IdParameters[paramName].Length == 0 && GuessStartingId(paramName, [.. graph.GetParameterIdOptions(paramName)]) is { } id)
                {
                    graph.IdParameters[paramName] = id;
                }
            }
        }

        private static string? GuessStartingId(string paramName, string[] options)
        {
            // Only the parameter's own idle: "rope_climb_idle" is an action like any other
            var idle = Array.Find(options, option => option == "idle" || option == $"{paramName}_idle");
            if (idle != null)
            {
                return idle;
            }

            if (paramName.Contains("type", StringComparison.OrdinalIgnoreCase))
            {
                return Array.Find(options, static option => option.EndsWith("_ground", StringComparison.Ordinal))
                    ?? Array.Find(options, static option => option == "default");
            }

            if (paramName.Contains("direction", StringComparison.OrdinalIgnoreCase))
            {
                return Array.Find(options, static option => option == "W");
            }

            return null;
        }

        // For parameters only read by code the graph does not describe, such as aim nodes
        private static FloatParameterRange? GuessFloatParameterRange(string paramName)
        {
            if (paramName.Contains("angle", StringComparison.OrdinalIgnoreCase))
            {
                return new FloatParameterRange(-180f, 180f, IsWholeNumber: false, IsDiscrete: false);
            }

            if (paramName.Contains("amount", StringComparison.OrdinalIgnoreCase))
            {
                return new FloatParameterRange(0f, 1f, IsWholeNumber: false, IsDiscrete: false);
            }

            return null;
        }

        private static AvaloniaControls.StackPanel CreateAnimGraphControls(AnimationGraph graph, List<GraphGizmoBinding> gizmoBindings)
        {
            var panel = new AvaloniaControls.StackPanel();
            panel.Classes.Add("sidebar");

            void Add(AvaloniaControls.Control control) => panel.Children.Add(control);

            void AddSection(string title, int count)
            {
                if (count > 0)
                {
                    Add(CreateSectionHeader(title));
                }
            }

            static IOrderedEnumerable<string> Sorted<T>(Dictionary<string, T> parameters)
                => parameters.Keys.Order(StringComparer.OrdinalIgnoreCase);

            AddSection("Values", graph.FloatParameters.Count);

            foreach (var paramName in Sorted(graph.FloatParameters))
            {
                var (editor, trailing) = CreateFloatParameterEditor(graph, paramName);
                Add(CreatePropertyRow(paramName, editor, trailing));
            }

            AddSection("Identifiers", graph.IdParameters.Count);

            foreach (var paramName in Sorted(graph.IdParameters))
            {
                // An empty ID is unset, which is how every ID parameter starts
                var options = graph.GetParameterIdOptions(paramName).Order(StringComparer.Ordinal).Prepend(UnsetIdText);
                var value = graph.IdParameters[paramName];

                Add(CreatePropertyRow(paramName,
                    CreateComboBox(options, value.Length == 0 ? UnsetIdText : value, id =>
                    {
                        graph.IdParameters[paramName] = id == UnsetIdText ? string.Empty : id;
                    })));
            }

            AddSection("Flags", graph.BoolParameters.Count);

            foreach (var paramName in Sorted(graph.BoolParameters))
            {
                // A held value is already true, so a pulse could not change it
                var signalButton = CreateButton("Signal", () => graph.SignalBoolParameter(paramName));
                signalButton.IsEnabled = !graph.BoolParameters[paramName];

                var checkBox = CreateCheckBox(graph.BoolParameters[paramName], isChecked =>
                {
                    graph.BoolParameters[paramName] = isChecked;
                    signalButton.IsEnabled = !isChecked;
                }, paramName);

                Add(CreatePropertyRow(string.Empty, checkBox, signalButton));
            }

            AddSection("Vectors", graph.VectorParameters.Count);

            foreach (var paramName in Sorted(graph.VectorParameters))
            {
                var binding = new GraphGizmoBinding(paramName, isTarget: false);
                var value = graph.VectorParameters[paramName];

                // Only look-at targets report a hint, so only they show a gizmo
                Add(CreatePropertyRow(paramName,
                    CreateCheckBox(binding.Gizmo.Visible, visible => binding.Gizmo.Visible = visible, "Gizmo"),
                    CreateButton("Reset", () => binding.ResetPending = true)));

                var vector = new Vector3(value.X, value.Y, value.Z);
                var fields = new AvaloniaControls.NumericUpDown[3];

                for (var i = 0; i < fields.Length; i++)
                {
                    var axis = i;
                    fields[i] = CreateFloatField(vector[i], 3, component =>
                    {
                        vector[axis] = component;
                        graph.VectorParameters[paramName] = new Vector4(vector, 0f);
                        binding.Initialized = true;
                    });
                    fields[i].Width = double.NaN;
                    fields[i].ShowButtonSpinner = false;
                }

                var row = new AvaloniaControls.Primitives.UniformGrid { Rows = 1, Margin = new(0, 1) };
                row.Children.AddRange(fields);

                binding.Fields = fields;
                Add(row);
                gizmoBindings.Add(binding);
            }

            AddSection("Targets", graph.TargetParameters.Count);

            foreach (var paramName in Sorted(graph.TargetParameters))
            {
                var binding = new GraphGizmoBinding(paramName, isTarget: true);

                // An unset target leaves the IK off and the gizmo on the animated bone
                Add(CreatePropertyRow(paramName,
                    CreateCheckBox(binding.Gizmo.Visible, visible => binding.Gizmo.Visible = visible, "Gizmo"),
                    CreateButton("Reset", () => graph.TargetParameters[paramName] = null)));

                gizmoBindings.Add(binding);
            }

            return panel;
        }

        private sealed class GraphGizmoBinding(string parameterName, bool isTarget)
        {
            public string ParameterName { get; } = parameterName;
            public bool IsTarget { get; } = isTarget;
            public TransformGizmos.Gizmo Gizmo { get; } = new(parameterName, allowRotation: isTarget);
            public AvaloniaControls.NumericUpDown[]? Fields { get; set; }
            public bool IsWorldSpace { get; set; }
            public volatile bool Initialized;
            public volatile bool ResetPending;
        }

        // Swapped with the active graph under the GL lock, synced into the gizmos on the render thread
        private List<GraphGizmoBinding> graphGizmoBindings = [];
        private List<GraphGizmoBinding>? shownGizmoBindings;
        private TransformGizmos? graphGizmos;

        // Written on the UI thread, read by the render loop
        private long gizmoMousePosition;
        private volatile bool gizmoPressPending;
        private volatile bool gizmoDragging;

        private ReadOnlyMemory<char> FormatGraphDebugText(AnimationGraph graph)
        {
            var capacity = GraphDebugTextCapacity + modelStatsText.Length;

            if (graphDebugText.Length < capacity)
            {
                graphDebugText = new char[capacity];
            }

            var text = graphDebugText.AsSpan(0, GraphDebugTextCapacity);
            var length = 0;
            int written;

            text.TryWrite($"AnimGraph2\n", out written);
            length += written;

            // Inclusive times of the graph and the graphs it references, indented by depth
            foreach (var timing in graph.GraphTimings)
            {
                var indent = Spaces.AsSpan(0, Math.Min(timing.Depth * 2, Spaces.Length));
                text[length..].TryWrite(CultureInfo.InvariantCulture, $"{indent}{timing.GraphName,-48} {timing.Duration.TotalMicroseconds,8:0.0} us\n", out written);
                length += written;
            }

            text[length..].TryWrite($"\n", out written);
            length += written;

            foreach (var clip in graph.SampledClips)
            {
                text[length..].TryWrite(CultureInfo.InvariantCulture, $"{clip.Name,-80} {clip.Time,6:0.00} / {clip.Duration:0.00}\n", out written);
                length += written;
            }

            text[length..].TryWrite($"\n", out written);
            length += written;

            modelStatsText.CopyTo(graphDebugText.AsSpan(length));
            return graphDebugText.AsMemory(0, length + modelStatsText.Length);
        }

        private const float GraphEventLingerSeconds = 1.5f;

        // Events of the last update, and those of earlier ones for a moment longer since most last a single update
        private readonly List<(SampledEvent Event, long LastSeen)> recentGraphEvents = [];
        private AnimationGraph? recentGraphEventsGraph;
        private long recentGraphEventsTime;

        private void UpdateRecentGraphEvents(AnimationGraph graph)
        {
            if (recentGraphEventsGraph != graph)
            {
                recentGraphEventsGraph = graph;
                recentGraphEvents.Clear();
            }

            var now = Stopwatch.GetTimestamp();
            recentGraphEventsTime = now;

            var events = graph.SampledEvents;

            for (var i = 0; i < events.Count; i++)
            {
                var sampled = events[i];
                var index = recentGraphEvents.Count - 1;

                // The same event from the same source is one line, however many branches sampled it
                while (index >= 0 && !IsSameGraphEvent(recentGraphEvents[index].Event, sampled))
                {
                    index--;
                }

                if (index < 0)
                {
                    recentGraphEvents.Add((sampled, now));
                }
                else if (recentGraphEvents[index].LastSeen != now || sampled.IsFromActiveBranch)
                {
                    recentGraphEvents[index] = (sampled, now);
                }
            }

            for (var i = recentGraphEvents.Count - 1; i >= 0; i--)
            {
                if (Stopwatch.GetElapsedTime(recentGraphEvents[i].LastSeen, now).TotalSeconds > GraphEventLingerSeconds)
                {
                    recentGraphEvents.RemoveAt(i);
                }
            }
        }

        private int shownGraphEventsHash;

        // Called every frame, but the list only gets new lines when an event starts, ends or lingers out
        private void UpdateGraphEventList(AnimationGraph graph)
        {
            if (graphEventList is not { } list)
            {
                return;
            }

            UpdateRecentGraphEvents(graph);

            var hash = new HashCode();

            foreach (var (sampled, lastSeen) in recentGraphEvents)
            {
                hash.Add(sampled.IsGraphEvent);
                hash.Add(sampled.GraphEventType);
                hash.Add(sampled.ID);
                hash.Add(sampled.AnimEvent);
                hash.Add(lastSeen == recentGraphEventsTime);
            }

            var newHash = hash.ToHashCode();

            if (newHash == shownGraphEventsHash)
            {
                return;
            }

            shownGraphEventsHash = newHash;

            var lines = new EventListControl.Line[recentGraphEvents.Count];

            for (var i = 0; i < lines.Length; i++)
            {
                var (sampled, lastSeen) = recentGraphEvents[i];
                var name = sampled.AnimEvent switch
                {
                    SoundEvent sound => sound.Name,
                    ParticleEvent particle => particle.ParticleSystem,
                    LegacyEvent legacy => legacy.AnimEventClassName,
                    _ => sampled.ID.Name,
                };

                // CNmSoundEvent reads as Sound
                var kind = sampled.AnimEvent is { } animEvent
                    ? animEvent.ClassName.Replace("CNm", string.Empty, StringComparison.Ordinal).Replace("Event", string.Empty, StringComparison.Ordinal)
                    : sampled.GraphEventType.ToString();

                lines[i] = new EventListControl.Line(name, kind, lastSeen == recentGraphEventsTime);
            }

            list.BeginInvoke(() => list.SetLines(lines));
        }

        private static bool IsSameGraphEvent(in SampledEvent a, in SampledEvent b)
            => a.IsGraphEvent == b.IsGraphEvent
            && a.GraphEventType == b.GraphEventType
            && a.ID == b.ID
            && a.AnimEvent == b.AnimEvent;

        private void ReadGizmoFromParameter(GraphGizmoBinding binding, Matrix4x4 nodeTransform)
        {
            Debug.Assert(animGraph != null);

            var gizmo = binding.Gizmo;
            var hasHint = animGraph.ParameterHints.TryGetValue(binding.ParameterName, out var hint);
            binding.IsWorldSpace = hasHint && hint.IsWorldSpace;

            FrameBone parameterValue;

            if (binding.IsTarget)
            {
                if (animGraph.TargetParameters.GetValueOrDefault(binding.ParameterName) is { } target)
                {
                    parameterValue = target;
                }
                else if (hasHint)
                {
                    parameterValue = hint.Transform;
                }
                else
                {
                    gizmo.HasValue = false;
                    return;
                }
            }
            else
            {
                if (!hasHint)
                {
                    gizmo.HasValue = false;
                    return;
                }

                // Look-at targets start ahead of the head rather than at the origin
                if (binding.ResetPending || !binding.Initialized)
                {
                    binding.ResetPending = false;
                    binding.Initialized = true;
                    animGraph.VectorParameters[binding.ParameterName] = new Vector4(hint.Transform.Position, 0f);
                    RefreshVectorFields(binding);
                }

                var vector = animGraph.VectorParameters[binding.ParameterName];
                parameterValue = new FrameBone(new Vector3(vector.X, vector.Y, vector.Z), 1f, Quaternion.Identity);
            }

            var characterValue = binding.IsWorldSpace ? parameterValue * animGraph.WorldTransform.Inverse() : parameterValue;

            if (!Matrix4x4.Decompose(characterValue.ToMatrix() * nodeTransform, out _, out var rotation, out var position))
            {
                gizmo.HasValue = false;
                return;
            }

            gizmo.Position = position;
            gizmo.Rotation = rotation;
            gizmo.HasValue = true;
        }

        private void WriteParameterFromGizmo(GraphGizmoBinding binding, Matrix4x4 nodeInverse)
        {
            Debug.Assert(animGraph != null);

            var gizmo = binding.Gizmo;
            var scene = Matrix4x4.CreateFromQuaternion(gizmo.Rotation) * Matrix4x4.CreateTranslation(gizmo.Position);

            if (!Matrix4x4.Decompose(scene * nodeInverse, out _, out var rotation, out var position))
            {
                return;
            }

            var characterValue = new FrameBone(position, 1f, rotation);
            var parameterValue = binding.IsWorldSpace ? characterValue * animGraph.WorldTransform : characterValue;

            if (binding.IsTarget)
            {
                animGraph.TargetParameters[binding.ParameterName] = parameterValue;
            }
            else
            {
                animGraph.VectorParameters[binding.ParameterName] = new Vector4(parameterValue.Position, 0f);
            }
        }

        private void RefreshVectorFields(GraphGizmoBinding binding)
        {
            if (animGraph == null || binding.Fields is not { } fields || binding.IsTarget)
            {
                return;
            }

            var vector = animGraph.VectorParameters[binding.ParameterName];
            Dispatcher.UIThread.Post(() =>
            {
                fields[0].Value = (decimal)vector.X;
                fields[1].Value = (decimal)vector.Y;
                fields[2].Value = (decimal)vector.Z;
            });
        }
    }
}
