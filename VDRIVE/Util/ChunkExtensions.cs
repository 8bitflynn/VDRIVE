namespace VDRIVE.Util
{
    public static class ChunkExtensions
    {
        public static IEnumerable<List<T>> BuildChunks<T>(this List<T> source, int chunkSize)
        {
            for (int i = 0; i < source.Count; i += chunkSize)
            {
                yield return source.GetRange(i, Math.Min(chunkSize, source.Count - i));
            }
        }

        public static short ComputeBatchCheckSum(this List<byte> batch)
        {
            short checkSum = 0;
            byte y_counter = 0;
            for (int i = 0; i < batch.Count; i++)
            {
                checkSum += batch[i];
                checkSum += y_counter;

                y_counter++;
            }

            return checkSum;
        }
    }
}
