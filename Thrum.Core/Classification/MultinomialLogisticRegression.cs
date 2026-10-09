namespace Thrum.Core.Classification;

/// <summary>
/// Ridge-regularized multinomial logistic regression (Softmax regression with L2 regularization).
/// Trained via gradient descent with momentum. Fast, deterministic, and fully offline.
/// </summary>
public sealed class MultinomialLogisticRegression
{
    public int NumClasses { get; set; }
    public int NumFeatures { get; set; }
    public float[] Weights { get; set; } = Array.Empty<float>(); // Flat array: NumClasses * NumFeatures
    public float[] Biases { get; set; } = Array.Empty<float>();  // NumClasses

    public MultinomialLogisticRegression() { }

    public MultinomialLogisticRegression(int numClasses, int numFeatures)
    {
        NumClasses = numClasses;
        NumFeatures = numFeatures;
        Weights = new float[numClasses * numFeatures];
        Biases = new float[numClasses];
    }

    /// <summary>
    /// Trains the multinomial logistic regression model on normalized training features.
    /// </summary>
    /// <param name="x">Training feature vectors (normalized).</param>
    /// <param name="y">Target class indices (0 to numClasses - 1).</param>
    /// <param name="numClasses">Number of distinct classes.</param>
    /// <param name="numFeatures">Feature dimension.</param>
    /// <param name="lambdaL2">Ridge L2 regularization strength (default: 0.01).</param>
    /// <param name="learningRate">Learning rate (default: 0.08).</param>
    /// <param name="epochs">Number of gradient descent iterations (default: 300).</param>
    public static MultinomialLogisticRegression Fit(
        IReadOnlyList<float[]> x,
        IReadOnlyList<int> y,
        int numClasses,
        int numFeatures,
        float lambdaL2 = 0.01f,
        float learningRate = 0.08f,
        int epochs = 300)
    {
        var model = new MultinomialLogisticRegression(numClasses, numFeatures);
        int n = x.Count;
        if (n == 0 || numClasses < 2) return model;

        float[] vWeights = new float[numClasses * numFeatures];
        float[] vBiases = new float[numClasses];
        float momentum = 0.9f;

        float[] gradW = new float[numClasses * numFeatures];
        float[] gradB = new float[numClasses];
        float[] probs = new float[numClasses];
        float[] logits = new float[numClasses];

        for (int epoch = 0; epoch < epochs; epoch++)
        {
            Array.Clear(gradW, 0, gradW.Length);
            Array.Clear(gradB, 0, gradB.Length);

            // Compute gradients over all samples
            for (int i = 0; i < n; i++)
            {
                var xi = x[i];
                int targetClass = y[i];

                // Compute logits
                float maxLogit = float.NegativeInfinity;
                for (int k = 0; k < numClasses; k++)
                {
                    float logit = model.Biases[k];
                    int wOffset = k * numFeatures;
                    for (int d = 0; d < numFeatures; d++)
                    {
                        logit += model.Weights[wOffset + d] * xi[d];
                    }
                    logits[k] = logit;
                    if (logit > maxLogit) maxLogit = logit;
                }

                // Compute softmax probabilities
                float sumExp = 0f;
                for (int k = 0; k < numClasses; k++)
                {
                    float exp = MathF.Exp(logits[k] - maxLogit);
                    probs[k] = exp;
                    sumExp += exp;
                }
                for (int k = 0; k < numClasses; k++)
                {
                    probs[k] /= sumExp;
                }

                // Accumulate cross-entropy gradients: (p_k - y_k)
                for (int k = 0; k < numClasses; k++)
                {
                    float error = probs[k] - (k == targetClass ? 1.0f : 0.0f);
                    gradB[k] += error;

                    int wOffset = k * numFeatures;
                    for (int d = 0; d < numFeatures; d++)
                    {
                        gradW[wOffset + d] += error * xi[d];
                    }
                }
            }

            // Apply L2 ridge regularization and update with momentum
            float invN = 1.0f / n;
            for (int k = 0; k < numClasses; k++)
            {
                // Bias update (no L2 penalty on bias)
                float gb = gradB[k] * invN;
                vBiases[k] = (momentum * vBiases[k]) - (learningRate * gb);
                model.Biases[k] += vBiases[k];

                int wOffset = k * numFeatures;
                for (int d = 0; d < numFeatures; d++)
                {
                    int idx = wOffset + d;
                    float gw = (gradW[idx] * invN) + (lambdaL2 * model.Weights[idx]);
                    vWeights[idx] = (momentum * vWeights[idx]) - (learningRate * gw);
                    model.Weights[idx] += vWeights[idx];
                }
            }
        }

        return model;
    }

    /// <summary>
    /// Computes class probabilities for a normalized feature vector.
    /// </summary>
    public void PredictProbabilities(ReadOnlySpan<float> normalizedVector, Span<float> probabilities)
    {
        if (NumClasses == 0) return;

        float maxLogit = float.NegativeInfinity;
        Span<float> logits = stackalloc float[NumClasses];

        for (int k = 0; k < NumClasses; k++)
        {
            float logit = Biases[k];
            int wOffset = k * NumFeatures;
            for (int d = 0; d < NumFeatures; d++)
            {
                logit += Weights[wOffset + d] * normalizedVector[d];
            }
            logits[k] = logit;
            if (logit > maxLogit) maxLogit = logit;
        }

        float sumExp = 0f;
        for (int k = 0; k < NumClasses; k++)
        {
            float exp = MathF.Exp(logits[k] - maxLogit);
            probabilities[k] = exp;
            sumExp += exp;
        }

        for (int k = 0; k < NumClasses; k++)
        {
            probabilities[k] /= sumExp;
        }
    }
}
