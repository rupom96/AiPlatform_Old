namespace AiPlatform.Application.Common.Text;

public static class VectorMath
{
    // Cosine similarity — দুই vector কতটা কাছাকাছি (1 = হুবহু, 0 = সম্পর্কহীন)।
    public static double CosineSimilarity(float[] a, float[] b)
    {
        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB) + 1e-9);
    }
}
