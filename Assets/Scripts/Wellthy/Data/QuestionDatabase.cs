using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using Wellthy.Config;

namespace Wellthy.Data
{
    /// <summary>
    /// Loads and queries the behavioral question bank (questions.json) and its corresponding MP3 audio clips.
    /// Works out-of-the-box whether files are placed in Assets/Resources/ or left in audio_gen/.
    /// </summary>
    public class QuestionDatabase : MonoBehaviour
    {
        [Header("Question Bank Source")]
        [Tooltip("Optional: Drag questions.json here. If left empty, loads automatically from Resources or audio_gen/questions.json.")]
        [SerializeField] private TextAsset questionsJsonAsset;

        [Tooltip("Resources subfolder for question MP3s if imported into Assets/Resources/ (e.g., 'Audio/Questions').")]
        [SerializeField] private string resourcesAudioFolder = "Audio/Questions";

        private readonly List<QuestionItem> _allQuestions = new List<QuestionItem>();
        private readonly Dictionary<string, QuestionItem> _questionsById = new Dictionary<string, QuestionItem>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _topics = new List<string>();
        private readonly Dictionary<string, List<QuestionItem>> _questionsByTopic = new Dictionary<string, List<QuestionItem>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, AudioClip> _audioCache = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);

        public bool IsLoaded { get; private set; }
        public IReadOnlyList<QuestionItem> AllQuestions => _allQuestions;
        public IReadOnlyList<string> Topics => _topics;

        private void Awake()
        {
            EnsureLoaded();
        }

        /// <summary>
        /// Loads questions.json if not already loaded.
        /// </summary>
        public void EnsureLoaded()
        {
            if (IsLoaded) return;

            string jsonText = ResolveQuestionsJsonText();
            if (string.IsNullOrWhiteSpace(jsonText))
            {
                Debug.LogError("[QuestionDatabase] Could not find questions.json in Inspector, Resources/questions, or audio_gen/questions.json.");
                return;
            }

            LoadFromJsonString(jsonText);
        }

        /// <summary>
        /// Parses question bank data directly from a JSON string matching audio_gen/questions.json.
        /// </summary>
        public void LoadFromJsonString(string jsonText)
        {
            _allQuestions.Clear();
            _questionsById.Clear();
            _topics.Clear();
            _questionsByTopic.Clear();

            QuestionBankRoot root = JsonUtility.FromJson<QuestionBankRoot>(jsonText);
            if (root == null || root.questions == null)
            {
                Debug.LogError("[QuestionDatabase] Failed to deserialize questions.json.");
                return;
            }

            foreach (QuestionItem item in root.questions)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.id)) continue;

                _allQuestions.Add(item);
                _questionsById[item.id] = item;

                string topic = string.IsNullOrWhiteSpace(item.topic) ? "General Behavioral" : item.topic.Trim();
                if (!_questionsByTopic.TryGetValue(topic, out List<QuestionItem> list))
                {
                    list = new List<QuestionItem>();
                    _questionsByTopic[topic] = list;
                    _topics.Add(topic);
                }
                list.Add(item);
            }

            IsLoaded = true;
            Debug.Log($"[QuestionDatabase] Loaded {_allQuestions.Count} questions across {_topics.Count} behavioral topics.");
        }

        public QuestionItem GetQuestionById(string id)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(id)) return null;
            _questionsById.TryGetValue(id.Trim(), out QuestionItem item);
            return item;
        }

        public List<QuestionItem> GetQuestionsByTopic(string topic)
        {
            EnsureLoaded();
            if (!string.IsNullOrWhiteSpace(topic) && _questionsByTopic.TryGetValue(topic.Trim(), out List<QuestionItem> list))
            {
                return new List<QuestionItem>(list);
            }
            return new List<QuestionItem>();
        }

        /// <summary>
        /// Builds the ordered list of questions for a session based on the user's InterviewConfig:
        /// - RandomMix: Shuffles all behavioral questions and takes up to numberOfQuestions.
        /// - ByTopic: Filters by the chosen behavioral topic(s), shuffles, and takes up to numberOfQuestions.
        /// - ExactSelection: Uses the exact questions picked by the user (up to numberOfQuestions).
        /// </summary>
        public List<QuestionItem> BuildSessionQueue(InterviewConfig config)
        {
            EnsureLoaded();

            if (config == null)
            {
                config = new InterviewConfig();
            }

            List<QuestionItem> pool = new List<QuestionItem>();

            switch (config.selectionMode)
            {
                case QuestionSelectionMode.ExactSelection:
                    if (config.selectedQuestionIds != null && config.selectedQuestionIds.Count > 0)
                    {
                        foreach (string id in config.selectedQuestionIds)
                        {
                            QuestionItem exact = GetQuestionById(id);
                            if (exact != null && !pool.Contains(exact))
                            {
                                pool.Add(exact);
                            }
                        }
                    }

                    if (pool.Count == 0)
                    {
                        Debug.LogWarning("[QuestionDatabase] No valid exact questions were selected; falling back to RandomMix.");
                        pool.AddRange(_allQuestions);
                        Shuffle(pool);
                        int fallbackCount = Mathf.Clamp(config.numberOfQuestions, 1, pool.Count);
                        return pool.GetRange(0, fallbackCount);
                    }

                    if (config.shuffleExactSelection)
                    {
                        Shuffle(pool);
                    }

                    int maxExact = Mathf.Clamp(config.numberOfQuestions, 1, pool.Count);
                    if (pool.Count > maxExact)
                    {
                        pool = pool.GetRange(0, maxExact);
                    }
                    return pool;

                case QuestionSelectionMode.ByTopic:
                    if (config.selectedTopics != null && config.selectedTopics.Count > 0)
                    {
                        HashSet<string> seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (string topic in config.selectedTopics)
                        {
                            if (string.IsNullOrWhiteSpace(topic)) continue;
                            if (_questionsByTopic.TryGetValue(topic.Trim(), out List<QuestionItem> topicQuestions))
                            {
                                foreach (QuestionItem q in topicQuestions)
                                {
                                    if (seenIds.Add(q.id))
                                    {
                                        pool.Add(q);
                                    }
                                }
                            }
                        }
                    }

                    if (pool.Count == 0)
                    {
                        Debug.LogWarning("[QuestionDatabase] No questions matched selected topics; falling back to all behavioral questions.");
                        pool.AddRange(_allQuestions);
                    }

                    Shuffle(pool);
                    int topicTargetCount = Mathf.Clamp(config.numberOfQuestions, 1, pool.Count);
                    return pool.GetRange(0, topicTargetCount);

                case QuestionSelectionMode.RandomMix:
                default:
                    pool.AddRange(_allQuestions);
                    Shuffle(pool);
                    if (pool.Count == 0) return pool;
                    int randomCount = Mathf.Clamp(config.numberOfQuestions, 1, pool.Count);
                    return pool.GetRange(0, randomCount);
            }
        }

        /// <summary>
        /// Loads the pre-generated MP3 AudioClip for a question ID (e.g., "beh_001").
        /// First checks Unity Resources, then falls back to audio_gen/generated_audio/{id}.mp3 on disk.
        /// </summary>
        public Coroutine LoadQuestionAudio(string questionId, Action<AudioClip> onLoaded)
        {
            return StartCoroutine(LoadQuestionAudioRoutine(questionId, onLoaded));
        }

        private IEnumerator LoadQuestionAudioRoutine(string questionId, Action<AudioClip> onLoaded)
        {
            if (string.IsNullOrWhiteSpace(questionId))
            {
                onLoaded?.Invoke(null);
                yield break;
            }

            if (_audioCache.TryGetValue(questionId, out AudioClip cached) && cached != null)
            {
                onLoaded?.Invoke(cached);
                yield break;
            }

            // 1. Try loading from Resources (e.g., Assets/Resources/Audio/Questions/beh_001.mp3)
            AudioClip clip = Resources.Load<AudioClip>($"{resourcesAudioFolder}/{questionId}");
            if (clip == null)
            {
                clip = Resources.Load<AudioClip>($"generated_audio/{questionId}");
            }
            if (clip == null)
            {
                clip = Resources.Load<AudioClip>(questionId);
            }

            if (clip != null)
            {
                _audioCache[questionId] = clip;
                onLoaded?.Invoke(clip);
                yield break;
            }

            // 2. Fallback: Load directly from audio_gen/generated_audio/{id}.mp3 on disk (great for independent testing)
            string diskPath = ResolveAudioFilePathOnDisk(questionId);
            if (!string.IsNullOrEmpty(diskPath) && File.Exists(diskPath))
            {
                string uri = new Uri(diskPath).AbsoluteUri;
                using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.MPEG))
                {
                    yield return www.SendWebRequest();

                    if (www.result == UnityWebRequest.Result.Success)
                    {
                        clip = DownloadHandlerAudioClip.GetContent(www);
                        if (clip != null)
                        {
                            clip.name = questionId;
                            _audioCache[questionId] = clip;
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[QuestionDatabase] Failed to load audio from {diskPath}: {www.error}");
                    }
                }
            }
            else
            {
                Debug.LogWarning($"[QuestionDatabase] Audio clip not found for '{questionId}'. Place MP3s in Assets/Resources/{resourcesAudioFolder}/ or audio_gen/generated_audio/.");
            }

            onLoaded?.Invoke(clip);
        }

        private string ResolveQuestionsJsonText()
        {
            if (questionsJsonAsset != null && !string.IsNullOrWhiteSpace(questionsJsonAsset.text))
            {
                return questionsJsonAsset.text;
            }

            TextAsset resourceAsset = Resources.Load<TextAsset>("questions");
            if (resourceAsset != null && !string.IsNullOrWhiteSpace(resourceAsset.text))
            {
                return resourceAsset.text;
            }

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string[] candidatePaths =
            {
                Path.Combine(projectRoot, "audio_gen", "questions.json"),
                Path.Combine(Application.dataPath, "audio_gen", "questions.json"),
                Path.Combine(Application.streamingAssetsPath, "questions.json")
            };

            foreach (string path in candidatePaths)
            {
                if (File.Exists(path))
                {
                    return File.ReadAllText(path);
                }
            }

            return null;
        }

        private string ResolveAudioFilePathOnDisk(string questionId)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string fileName = $"{questionId}.mp3";

            string[] candidatePaths =
            {
                Path.Combine(projectRoot, "audio_gen", "generated_audio", fileName),
                Path.Combine(Application.dataPath, "audio_gen", "generated_audio", fileName),
                Path.Combine(Application.streamingAssetsPath, "generated_audio", fileName)
            };

            foreach (string path in candidatePaths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return null;
        }

        private static void Shuffle<T>(IList<T> list)
        {
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = UnityEngine.Random.Range(0, n + 1);
                T value = list[k];
                list[k] = list[n];
                list[n] = value;
            }
        }
    }
}
