using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace BetterFileSys.Services
{
    /// <summary>
    /// M2: The Brain - Embedding service using ONNX Runtime
    /// Converts text queries and file content into numerical vectors (embeddings)
    /// Using all-MiniLM-L6-v2 model (384-dimensional vectors)
    /// </summary>
    public class EmbeddingService : IDisposable
    {
        private readonly string _logPath = Path.Combine(Path.GetTempPath(), "VaultRecon_Debug.log");
        private InferenceSession? _session;
        private BertTokenizer? _tokenizer;
        private bool _isInitialized = false;

        public bool IsInitialized => _isInitialized;

        private void Log(string message)
        {
            try
            {
                File.AppendAllText(_logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\n");
                Console.WriteLine(message);
            }
            catch { }
        }

        /// <summary>
        /// Initialize embedding service, download vocab/model if missing, and load model
        /// </summary>
        public async Task InitializeAsync(Action<string>? progressCallback = null)
        {
            await Task.Run(async () =>
            {
                try
                {
                    Log("[EMBED] Initializing ONNX Runtime embedding service");

                    string modelPath = GetModelPath();
                    string vocabPath = GetVocabPath();
                    string modelsDirectory = Path.GetDirectoryName(modelPath)!;

                    if (!Directory.Exists(modelsDirectory))
                    {
                        Directory.CreateDirectory(modelsDirectory);
                    }

                    using (var httpClient = new HttpClient())
                    {
                        httpClient.Timeout = TimeSpan.FromMinutes(10);

                        // 1. Download vocab.txt if it doesn't exist
                        if (!File.Exists(vocabPath))
                        {
                            Log("[EMBED] vocab.txt not found. Starting download...");
                            progressCallback?.Invoke("Downloading vocabulary (vocab.txt)...");
                            await DownloadFileWithProgressAsync(
                                httpClient,
                                "https://huggingface.co/nixiesearch/all-MiniLM-L6-v2-onnx/resolve/main/vocab.txt",
                                vocabPath,
                                "vocab.txt",
                                progressCallback
                            );
                            Log("[EMBED] vocab.txt downloaded successfully");
                        }

                        // 2. Download model.onnx if it doesn't exist
                        if (!File.Exists(modelPath))
                        {
                            Log("[EMBED] model.onnx not found. Starting download...");
                            progressCallback?.Invoke("Downloading AI model (model.onnx, ~90MB)...");
                            await DownloadFileWithProgressAsync(
                                httpClient,
                                "https://huggingface.co/nixiesearch/all-MiniLM-L6-v2-onnx/resolve/main/model.onnx",
                                modelPath,
                                "model.onnx",
                                progressCallback
                            );
                            Log("[EMBED] model.onnx downloaded successfully");
                        }
                    }

                    // 3. Initialize Tokenizer
                    progressCallback?.Invoke("Loading vocabulary...");
                    _tokenizer = new BertTokenizer(vocabPath);

                    // 4. Load ONNX model
                    progressCallback?.Invoke("Loading AI model into memory...");
                    var sessionOptions = new SessionOptions();
                    sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
                    sessionOptions.IntraOpNumThreads = Math.Min(2, Environment.ProcessorCount);

                    _session = new InferenceSession(modelPath, sessionOptions);
                    _isInitialized = true;
                    Log($"[EMBED] ONNX model loaded successfully: {modelPath}");
                    progressCallback?.Invoke("AI system ready");
                }
                catch (Exception ex)
                {
                    Log($"[EMBED] Initialization error: {ex.Message}");
                    _isInitialized = false;
                    progressCallback?.Invoke($"AI Initialization failed: {ex.Message}");
                    throw;
                }
            });
        }

        /// <summary>
        /// Helper to download file with chunk-by-chunk progress callback
        /// </summary>
        private async Task DownloadFileWithProgressAsync(
            HttpClient client,
            string url,
            string destinationPath,
            string fileName,
            Action<string>? progressCallback)
        {
            string tempPath = destinationPath + ".tmp";
            try
            {
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                long totalBytes = response.Content.Headers.ContentLength ?? -1L;
                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                byte[] buffer = new byte[8192];
                long totalRead = 0L;
                int read;

                while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, read);
                    totalRead += read;

                    if (progressCallback != null)
                    {
                        if (totalBytes > 0)
                        {
                            double percentage = (double)totalRead / totalBytes * 100;
                            progressCallback($"Downloading {fileName}: {percentage:F1}%");
                        }
                        else
                        {
                            double mbRead = (double)totalRead / (1024 * 1024);
                            progressCallback($"Downloading {fileName}: {mbRead:F2} MB");
                        }
                    }
                }

                await fileStream.FlushAsync();
                fileStream.Close();

                if (File.Exists(destinationPath))
                {
                    File.Delete(destinationPath);
                }
                File.Move(tempPath, destinationPath);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
            }
        }

        /// <summary>
        /// Generate embedding vector for text
        /// Returns 384-dimensional vector for all-MiniLM-L6-v2
        /// </summary>
        public async Task<float[]?> GetEmbeddingAsync(string text)
        {
            if (string.IsNullOrEmpty(text))
                return Array.Empty<float>();

            return await Task.Run(() =>
            {
                try
                {
                    if (!_isInitialized || _session == null || _tokenizer == null)
                    {
                        Log($"[EMBED] Embedding service not initialized - using fallback search");
                        return null;
                    }

                    // Tokenize and prepare input
                    var tokens = Tokenize(text);
                    Log($"[EMBED] Generated {tokens.Length} tokens from text");
                    var inputIds = PrepareInputTensor(tokens);
                    var attentionMask = PrepareAttentionMask(tokens);
                    var tokenTypeIds = PrepareTokenTypeIds(tokens);

                    // Run inference with all required inputs (INT64)
                    var inputs = new List<NamedOnnxValue>
                    {
                        NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
                        NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask),
                        NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIds)
                    };

                    Log($"[EMBED] Running inference...");
                    using (var results = _session.Run(inputs))
                    {
                        // Extract raw output (last_hidden_state)
                        var rawOutput = results.FirstOrDefault()?.AsEnumerable<float>().ToArray();
                        Log($"[EMBED] Inference successful, got raw output of length {(rawOutput?.Length ?? 0)}");
                        
                        if (rawOutput != null && rawOutput.Length == 512 * 384)
                        {
                            // Mean Pooling across sequence length using token count
                            int validTokens = Math.Min(tokens.Length, 512);
                            if (validTokens == 0)
                                validTokens = 1;

                            float[] pooled = new float[384];
                            for (int dim = 0; dim < 384; dim++)
                            {
                                float sum = 0f;
                                for (int t = 0; t < validTokens; t++)
                                {
                                    int index = (t * 384) + dim;
                                    sum += rawOutput[index];
                                }
                                pooled[dim] = sum / validTokens;
                            }

                            // Normalize to unit vector
                            return NormalizeVector(pooled);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"[EMBED] Error generating embedding: {ex.Message}");
                }

                return null;
            });
        }

        /// <summary>
        /// Calculate cosine similarity between two vectors (0 to 1)
        /// </summary>
        public static double CosineSimilarity(float[] vec1, float[] vec2)
        {
            if (vec1 == null || vec2 == null || vec1.Length == 0 || vec2.Length == 0)
                return 0.0;

            if (vec1.Length != vec2.Length)
                return 0.0;

            double dotProduct = 0.0;
            double mag1 = 0.0;
            double mag2 = 0.0;

            for (int i = 0; i < vec1.Length; i++)
            {
                dotProduct += vec1[i] * vec2[i];
                mag1 += vec1[i] * vec1[i];
                mag2 += vec2[i] * vec2[i];
            }

            mag1 = Math.Sqrt(mag1);
            mag2 = Math.Sqrt(mag2);

            if (mag1 == 0 || mag2 == 0)
                return 0.0;

            return dotProduct / (mag1 * mag2);
        }

        /// <summary>
        /// Tokenize text to token IDs using the real BertTokenizer
        /// </summary>
        private int[] Tokenize(string text)
        {
            if (_tokenizer == null)
            {
                return Array.Empty<int>();
            }
            return _tokenizer.Tokenize(text);
        }

        /// <summary>
        /// Prepare input_ids tensor for ONNX model (Int64)
        /// </summary>
        private DenseTensor<long> PrepareInputTensor(int[] tokenIds)
        {
            const int maxSequenceLength = 512;
            var paddedTokens = new long[maxSequenceLength];

            for (int i = 0; i < Math.Min(tokenIds.Length, maxSequenceLength); i++)
            {
                paddedTokens[i] = tokenIds[i];
            }

            return new DenseTensor<long>(paddedTokens, new[] { 1, maxSequenceLength });
        }

        /// <summary>
        /// Prepare attention_mask tensor (1 for real tokens, 0 for padding) - Int64
        /// </summary>
        private DenseTensor<long> PrepareAttentionMask(int[] tokenIds)
        {
            const int maxSequenceLength = 512;
            var mask = new long[maxSequenceLength];

            for (int i = 0; i < Math.Min(tokenIds.Length, maxSequenceLength); i++)
            {
                mask[i] = 1;
            }

            return new DenseTensor<long>(mask, new[] { 1, maxSequenceLength });
        }

        /// <summary>
        /// Prepare token_type_ids tensor (all 0s for single sequence) - Int64
        /// </summary>
        private DenseTensor<long> PrepareTokenTypeIds(int[] tokenIds)
        {
            const int maxSequenceLength = 512;
            var typeIds = new long[maxSequenceLength];
            return new DenseTensor<long>(typeIds, new[] { 1, maxSequenceLength });
        }

        /// <summary>
        /// Normalize vector to unit length
        /// </summary>
        private float[] NormalizeVector(float[] vector)
        {
            if (vector == null || vector.Length == 0)
                return Array.Empty<float>();

            double magnitude = Math.Sqrt(vector.Sum(v => v * v));
            
            if (magnitude == 0)
                return vector;

            return vector.Select(v => (float)(v / magnitude)).ToArray();
        }

        /// <summary>
        /// Get path to embedding model
        /// </summary>
        private string GetModelPath()
        {
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VaultRecon",
                "models"
            );

            return Path.Combine(appDataPath, "all-MiniLM-L6-v2.onnx");
        }

        /// <summary>
        /// Get path to vocabulary file
        /// </summary>
        private string GetVocabPath()
        {
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VaultRecon",
                "models"
            );

            return Path.Combine(appDataPath, "vocab.txt");
        }

        public void Dispose()
        {
            _session?.Dispose();
            _isInitialized = false;
        }
    }
}
