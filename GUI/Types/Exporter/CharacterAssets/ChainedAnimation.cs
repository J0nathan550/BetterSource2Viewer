using System.IO;
using System.Linq;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelFlex;

namespace GUI.Types.Exporter.CharacterAssets
{
    /// <summary>
    /// Looping animations played one after another as one loop, e.g. a run that slides now and then. The server only
    /// sends one number for a sequence the original has a single version of, so the versions the game would pick
    /// between have to take turns within it. Each fades in over the one before like the game fades between sequences,
    /// which keeps playing as it loops, since they rarely start on the same step.
    /// </summary>
    sealed class ChainedAnimation : AnimationBase
    {
        // The fade ModelDoc gives sequences by default
        private const float FadeTime = 0.2f;

        private readonly AnimationBase[] Parts;
        private readonly Skeleton Skeleton;
        private readonly FlexController[] FlexControllers;
        private readonly int FadeFrames;

        /// <param name="name">The name the chain is written as.</param>
        /// <param name="parts">The animations in the order they play, at one frame rate and without root motion.</param>
        /// <param name="skeleton">The skeleton of the model the animations are of.</param>
        /// <param name="flexControllers">The flex controllers of the model the animations are of.</param>
        public ChainedAnimation(string name, IReadOnlyList<AnimationBase> parts, Skeleton skeleton, FlexController[] flexControllers)
        {
            if (parts.Count == 0)
            {
                throw new ArgumentException("A chain needs at least one animation", nameof(parts));
            }

            foreach (var part in parts)
            {
                if (part.CycleFrames <= 0 || part.IsAdditive || part.HasMovementData())
                {
                    throw new InvalidDataException($"{part.Name} is not a looping pose animation without root motion");
                }

                if (MathF.Abs(part.Fps - parts[0].Fps) > 0.01f)
                {
                    throw new InvalidDataException($"{part.Name} plays at {part.Fps} fps, {parts[0].Name} at {parts[0].Fps}");
                }
            }

            Parts = [.. parts];
            Skeleton = skeleton;
            FlexControllers = flexControllers;
            Name = name;
            Fps = parts[0].Fps;
            FrameCount = parts.Sum(static part => part.CycleFrames) + 1;
            TargetSkeletonName = parts[0].TargetSkeletonName;
            FadeFrames = parts.Count > 1 ? Math.Min((int)MathF.Round(FadeTime * Fps), parts.Min(static part => part.CycleFrames)) : 0;
        }

        /// <summary>The frame each part starts on, in the order they play.</summary>
        public IEnumerable<int> PartStartFrames
        {
            get
            {
                var start = 0;

                foreach (var part in Parts)
                {
                    yield return start;
                    start += part.CycleFrames;
                }
            }
        }

        /// <inheritdoc/>
        public override bool HasFlexData => Parts.Any(static part => part.HasFlexData);

        /// <inheritdoc/>
        public override void DecodeFrame(Frame outFrame)
        {
            var chainFrame = outFrame.FrameIndex;
            var (index, frame) = Locate(chainFrame);

            outFrame.FrameIndex = frame;

            try
            {
                Parts[index].DecodeFrame(outFrame);
            }
            finally
            {
                outFrame.FrameIndex = chainFrame;
            }

            if (frame >= FadeFrames)
            {
                return;
            }

            var previous = Parts[(index + Parts.Length - 1) % Parts.Length];
            var fading = new Frame(Skeleton, FlexControllers) { FrameIndex = frame % previous.CycleFrames };
            previous.DecodeFrame(fading);

            var weight = (float)frame / FadeFrames;

            for (var i = 0; i < outFrame.Bones.Length; i++)
            {
                outFrame.Bones[i] = fading.Bones[i].Blend(outFrame.Bones[i], weight);
            }

            for (var i = 0; i < outFrame.Datas.Length; i++)
            {
                outFrame.Datas[i] = float.Lerp(fading.Datas[i], outFrame.Datas[i], weight);
            }
        }

        /// <inheritdoc/>
        public override FrameBone GetAdditiveDelta(int boneIndex, FrameBone bone) => Parts[0].GetAdditiveDelta(boneIndex, bone);

        /// <inheritdoc/>
        public override bool HasMovementData() => false;

        /// <inheritdoc/>
        public override AnimationMovement.MovementData GetMovementOffsetData(float time) => default;

        /// <inheritdoc/>
        public override AnimationMovement.MovementData GetMovementOffsetData(int frame) => default;

        // Each part's last frame is where it loops back to its first, so the next part's first frame stands in for it,
        // and the chain's own last frame is where it loops back to the first part
        private (int Index, int Frame) Locate(int frame)
        {
            for (var i = 0; i < Parts.Length; i++)
            {
                if (frame < Parts[i].CycleFrames)
                {
                    return (i, Math.Max(frame, 0));
                }

                frame -= Parts[i].CycleFrames;
            }

            return (0, 0);
        }
    }
}
