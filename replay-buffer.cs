using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace chicago_express_bot
{
    public sealed record PendingReplayTransition(
        GameState State,
        float[] StateFeatures,
        CandidateAction ChosenAction,
        float[] ChosenActionFeatures);

    public sealed record ReplayTransition(
        float[] StateFeatures,
        float[] ChosenActionFeatures,
        float Reward,
        float[] NextStateFeatures,
        IReadOnlyList<float[]> NextLegalActionsFeatures,
        bool IsTerminal
    );


    public sealed class ReplayBuffer
    {
        private readonly ReplayTransition?[] _buffer;
        private readonly Random _random;
        private int _nextIndex;
        private int _count;

        public int Count => _count;
        public int Capacity => _buffer.Length;

        public ReplayBuffer(int capacity, int? seed = null)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(capacity));

            _buffer = new ReplayTransition[capacity];
            _random = seed.HasValue
                ? new Random(seed.Value)
                : new Random();
        }

        public void Add(ReplayTransition transition)
        {
            ArgumentNullException.ThrowIfNull(transition);

            _buffer[_nextIndex] = transition;

            _nextIndex = (_nextIndex + 1) % Capacity;
            _count = Math.Min(_count + 1, Capacity);
        }

        public ReplayTransition[] Sample(int batchSize)
        {
            if (batchSize <= 0 || batchSize > _count)
                throw new ArgumentOutOfRangeException(
                    nameof(batchSize),
                    "Batch size must be between 1 and Count.");

            // Sample without replacement.
            int[] indices = Enumerable.Range(0, _count).ToArray();

            for (int i = 0; i < batchSize; i++)
            {
                int j = _random.Next(i, _count);
                (indices[i], indices[j]) =
                    (indices[j], indices[i]);
            }

            var batch = new ReplayTransition[batchSize];

            for (int i = 0; i < batchSize; i++)
                batch[i] = _buffer[indices[i]]!;

            return batch;
        }

        internal void WriteTo(BinaryWriter writer)
        {
            writer.Write(Capacity);
            writer.Write(_nextIndex);
            writer.Write(_count);

            for (int i = 0; i < _buffer.Length; i++)
            {
                ReplayTransition? transition = _buffer[i];
                writer.Write(transition is not null);

                if (transition is null)
                    continue;

                WriteFloatArray(writer, transition.StateFeatures);
                WriteFloatArray(writer, transition.ChosenActionFeatures);
                writer.Write(transition.Reward);
                WriteFloatArray(writer, transition.NextStateFeatures);

                writer.Write(transition.NextLegalActionsFeatures.Count);
                foreach (float[] actionFeatures in transition.NextLegalActionsFeatures)
                    WriteFloatArray(writer, actionFeatures);

                writer.Write(transition.IsTerminal);
            }
        }

        internal static ReplayBuffer ReadFrom(BinaryReader reader)
        {
            int capacity = reader.ReadInt32();
            int nextIndex = reader.ReadInt32();
            int count = reader.ReadInt32();

            if (capacity <= 0)
                throw new InvalidDataException("Replay buffer capacity is invalid.");

            if (nextIndex < 0 || nextIndex >= capacity)
                throw new InvalidDataException("Replay buffer index is invalid.");

            if (count < 0 || count > capacity)
                throw new InvalidDataException("Replay buffer count is invalid.");

            var replayBuffer = new ReplayBuffer(capacity)
            {
                _nextIndex = nextIndex,
                _count = count
            };

            int actualCount = 0;

            for (int i = 0; i < capacity; i++)
            {
                if (!reader.ReadBoolean())
                    continue;

                replayBuffer._buffer[i] = new ReplayTransition(
                    StateFeatures: ReadFloatArray(reader),
                    ChosenActionFeatures: ReadFloatArray(reader),
                    Reward: reader.ReadSingle(),
                    NextStateFeatures: ReadFloatArray(reader),
                    NextLegalActionsFeatures: ReadActionFeatures(reader),
                    IsTerminal: reader.ReadBoolean());

                actualCount++;
            }

            if (actualCount != count)
                throw new InvalidDataException(
                    "Replay buffer entry count does not match the stored count.");

            return replayBuffer;
        }

        private static void WriteFloatArray(BinaryWriter writer, float[] values)
        {
            writer.Write(values.Length);

            foreach (float value in values)
                writer.Write(value);
        }

        private static float[] ReadFloatArray(BinaryReader reader)
        {
            int length = reader.ReadInt32();

            if (length < 0)
                throw new InvalidDataException("Float array length is invalid.");

            var values = new float[length];

            for (int i = 0; i < length; i++)
                values[i] = reader.ReadSingle();

            return values;
        }

        private static IReadOnlyList<float[]> ReadActionFeatures(BinaryReader reader)
        {
            int count = reader.ReadInt32();

            if (count < 0)
                throw new InvalidDataException("Action feature count is invalid.");

            var actionFeatures = new List<float[]>(count);

            for (int i = 0; i < count; i++)
                actionFeatures.Add(ReadFloatArray(reader));

            return actionFeatures;
        }
    }

}
