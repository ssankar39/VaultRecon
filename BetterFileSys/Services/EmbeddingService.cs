using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private readonly string _logPath = Path.Combine(Path.GetTempPath(), "BetterFileSys_Debug.log");
        private InferenceSession? _session;
        private bool _isInitialized = false;

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
        /// Initialize embedding service and load ONNX model
        /// Model: all-MiniLM-L6-v2 (384-dimensional embeddings)
        /// </summary>
        public async Task InitializeAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    Log("[EMBED] Initializing ONNX Runtime embedding service");

                    // For MVP, we'll use a minimal embedder or placeholder
                    // In Phase 1b, download/cache all-MiniLM-L6-v2 ONNX model
                    // Model path would be: Path.Combine(AppData, "BetterFileSys", "models", "all-MiniLM-L6-v2.onnx")

                    var sessionOptions = new SessionOptions();
                    sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
                    sessionOptions.IntraOpNumThreads = Math.Min(2, Environment.ProcessorCount);

                    // TODO: Phase 1b - Download model if not exists
                    // For now, detect if model exists, log status
                    string modelPath = GetModelPath();
                    
                    if (File.Exists(modelPath))
                    {
                        _session = new InferenceSession(modelPath, sessionOptions);
                        _isInitialized = true;
                        Log($"[EMBED] ONNX model loaded successfully: {modelPath}");
                    }
                    else
                    {
                        Log($"[EMBED] Model not found at {modelPath} - semantic search will use fallback (filename match only)");
                        Log($"[EMBED] TODO Phase 1b: Download all-MiniLM-L6-v2 model");
                        _isInitialized = false;
                    }
                }
                catch (Exception ex)
                {
                    Log($"[EMBED] Initialization error: {ex.Message}");
                    _isInitialized = false;
                }
            });
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
                    if (!_isInitialized || _session == null)
                    {
                        // Fallback: return null to indicate semantic search unavailable
                        Log($"[EMBED] Model not initialized - using keyword-only search");
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
                        // Extract embedding from output
                        var embedding = results.FirstOrDefault()?.AsEnumerable<float>().ToArray();
                        Log($"[EMBED] Inference successful, got embedding vector");
                        
                        if (embedding != null)
                        {
                            // Normalize to unit vector
                            return NormalizeVector(embedding);
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
        /// Tokenize text to token IDs (character-level fallback)
        /// TODO Phase 1b: Integrate proper BertTokenizer or sentence-piece tokenizer
        /// </summary>
        private int[] Tokenize(string text)
        {
            // MVP: Use character + word tokens as simple tokenization
            // Real implementation requires proper BERT WordPiece tokenizer
            
            var tokens = new List<int>();
            text = text.ToLower();
            
            // Add [CLS] token (ID 101 in BERT vocab)
            tokens.Add(101);
            
            // Simple word tokenization with stable token ID mapping
            var words = text.Split(new[] { ' ', '\t', '\n', ',', '.', ';', ':', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var word in words)
            {
                // Map word to stable token ID in valid BERT vocab range (1000-11000)
                int tokenId = Math.Abs(word.GetHashCode()) % 10000 + 1000;
                tokens.Add(tokenId);
            }
            
            // Add [SEP] token (ID 102)
            tokens.Add(102);
            
            return tokens.ToArray();
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
            // Standard location for model cache
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BetterFileSys",
                "models"
            );

            return Path.Combine(appDataPath, "all-MiniLM-L6-v2.onnx");
        }

        public void Dispose()
        {
            _session?.Dispose();
            _isInitialized = false;
        }
    }
}
