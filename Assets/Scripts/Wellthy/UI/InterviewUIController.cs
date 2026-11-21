using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Wellthy.Config;
using Wellthy.Core;
using Wellthy.Data;
using Wellthy.Evaluation;

namespace Wellthy.UI
{
    /// <summary>
    /// Connects a Unity Canvas (Screen-Space or VR World-Space) to InterviewSessionManager.
    /// All serialized UI fields are optional so you can wire up panels incrementally.
    /// </summary>
    public class InterviewUIController : MonoBehaviour
    {
        [Header("Core Manager")]
        [SerializeField] private InterviewSessionManager sessionManager;

        [Header("Screen Panels")]
        [SerializeField] private GameObject setupPanel;
        [SerializeField] private GameObject interviewPanel;
        [SerializeField] private GameObject selfEvaluationPanel;
        [SerializeField] private GameObject goodOutcomePanel;
        [SerializeField] private GameObject badOutcomePanel;

        [Header("1. Setup UI Controls")]
        [SerializeField] private Slider questionCountSlider;
        [SerializeField] private Text questionCountValueText;
        [SerializeField] private Button duration5MinButton;
        [SerializeField] private Button duration15MinButton;
        [SerializeField] private Button duration45MinButton;
        [SerializeField] private Text selectedDurationText;
        [SerializeField] private Dropdown selectionModeDropdown;
        [SerializeField] private Dropdown topicDropdown;
        [SerializeField] private Button startInterviewButton;

        [Header("2. Active Interview HUD Controls")]
        [SerializeField] private Text timerText;
        [SerializeField] private Text questionProgressText;
        [SerializeField] private Text questionTopicText;
        [SerializeField] private Text questionPromptText;
        [SerializeField] private Button beginQuestionButton;
        [SerializeField] private Button nextQuestionButton;
        [SerializeField] private Button replayAudioButton;
        [SerializeField] private Button endSessionButton;

        [Header("3. Self-Evaluation & Rubric Controls")]
        [SerializeField] private Text evaluationSummaryText;
        [SerializeField] private Text rubricHintsDisplayText;
        [SerializeField] private Slider situationTaskSlider;
        [SerializeField] private Slider actionSpecificitySlider;
        [SerializeField] private Slider resultImpactSlider;
        [SerializeField] private Slider pacingConfidenceSlider;
        [SerializeField] private InputField reflectionNotesInput;
        [SerializeField] private Button interviewWentGoodButton;
        [SerializeField] private Button interviewWentBadButton;

        [Header("4. Outcome Controls")]
        [SerializeField] private Text feedbackTipsText;
        [SerializeField] private Button tryAgainFromBadButton;
        [SerializeField] private Button practiceAgainFromGoodButton;

        private void Awake()
        {
            if (sessionManager == null)
            {
                sessionManager = GetComponent<InterviewSessionManager>();
                if (sessionManager == null)
                {
#if UNITY_2022_2_OR_NEWER
                    sessionManager = FindFirstObjectByType<InterviewSessionManager>();
#else
                    sessionManager = FindObjectOfType<InterviewSessionManager>();
#endif
                }
            }
        }

        private void Start()
        {
            if (sessionManager == null)
            {
                Debug.LogError("[InterviewUIController] No InterviewSessionManager found in scene.");
                return;
            }

            BindButtonsAndInputs();
            PopulateTopicDropdown();
            RefreshSetupLabels();
            HandleStateChanged(sessionManager.CurrentState);
        }

        private void OnEnable()
        {
            if (sessionManager == null) return;

            sessionManager.onStateChanged.AddListener(HandleStateChanged);
            sessionManager.onQuestionPresented.AddListener(HandleQuestionPresented);
            sessionManager.onTimerUpdated.AddListener(HandleTimerUpdated);
            sessionManager.onSessionEnded.AddListener(HandleSessionEnded);

            if (sessionManager.Evaluation != null)
            {
                sessionManager.Evaluation.onBadOutcomeSelected.AddListener(HandleBadOutcomeTips);
            }
        }

        private void OnDisable()
        {
            if (sessionManager == null) return;

            sessionManager.onStateChanged.RemoveListener(HandleStateChanged);
            sessionManager.onQuestionPresented.RemoveListener(HandleQuestionPresented);
            sessionManager.onTimerUpdated.RemoveListener(HandleTimerUpdated);
            sessionManager.onSessionEnded.RemoveListener(HandleSessionEnded);

            if (sessionManager.Evaluation != null)
            {
                sessionManager.Evaluation.onBadOutcomeSelected.RemoveListener(HandleBadOutcomeTips);
            }
        }

        // --- Public UI Button Callbacks (Can also be wired directly in the Unity Inspector) ---

        public void SelectDuration5Minutes()
        {
            sessionManager.ActiveConfig.SetDurationMinutes(5);
            RefreshSetupLabels();
        }

        public void SelectDuration15Minutes()
        {
            sessionManager.ActiveConfig.SetDurationMinutes(15);
            RefreshSetupLabels();
        }

        public void SelectDuration45Minutes()
        {
            sessionManager.ActiveConfig.SetDurationMinutes(45);
            RefreshSetupLabels();
        }

        public void SetQuestionCountFromSlider(float value)
        {
            sessionManager.ActiveConfig.SetQuestionCount(Mathf.RoundToInt(value));
            RefreshSetupLabels();
        }

        public void SetSelectionModeIndex(int modeIndex)
        {
            sessionManager.ActiveConfig.selectionMode = (QuestionSelectionMode)Mathf.Clamp(modeIndex, 0, 2);
            RefreshSetupLabels();
        }

        public void SelectSingleTopicByIndex(int topicIndex)
        {
            IReadOnlyList<string> topics = sessionManager.Database.Topics;
            if (topicIndex < 0 || topicIndex >= topics.Count) return;

            sessionManager.ActiveConfig.selectedTopics.Clear();
            sessionManager.ActiveConfig.selectedTopics.Add(topics[topicIndex]);
            RefreshSetupLabels();
        }

        public void ToggleExactQuestionSelection(string questionId)
        {
            sessionManager.ActiveConfig.ToggleExactQuestionId(questionId);
            if (sessionManager.ActiveConfig.selectionMode == QuestionSelectionMode.ExactSelection &&
                sessionManager.ActiveConfig.selectedQuestionIds.Count > 0)
            {
                sessionManager.ActiveConfig.SetQuestionCount(sessionManager.ActiveConfig.selectedQuestionIds.Count);
            }
            RefreshSetupLabels();
        }

        public void OnClickStartInterview() => sessionManager.StartSession();
        public void OnClickBeginQuestion()
        {
            sessionManager.BeginAnsweringCurrentQuestion();
            RefreshActiveInterviewButtons();
        }
        public void OnClickNextQuestion() => sessionManager.CompleteCurrentQuestionAndAdvance();
        public void OnClickReplayQuestionAudio() => sessionManager.ReplayCurrentQuestionAudio();
        public void OnClickEndSession() => sessionManager.EndSessionManually();
        public void OnClickSubmitGoodOutcome() => SyncSlidersAndSubmitEvaluation(true);
        public void OnClickSubmitBadOutcome() => SyncSlidersAndSubmitEvaluation(false);
        public void OnClickReturnToSetup() => sessionManager.ReturnToSetup();

        // --- Internal Event Handlers ---

        private void BindButtonsAndInputs()
        {
            if (questionCountSlider != null)
            {
                questionCountSlider.minValue = 1;
                questionCountSlider.maxValue = 25;
                questionCountSlider.wholeNumbers = true;
                questionCountSlider.value = sessionManager.ActiveConfig.numberOfQuestions;
                questionCountSlider.onValueChanged.AddListener(SetQuestionCountFromSlider);
            }

            if (duration5MinButton != null) duration5MinButton.onClick.AddListener(SelectDuration5Minutes);
            if (duration15MinButton != null) duration15MinButton.onClick.AddListener(SelectDuration15Minutes);
            if (duration45MinButton != null) duration45MinButton.onClick.AddListener(SelectDuration45Minutes);

            if (selectionModeDropdown != null)
            {
                selectionModeDropdown.ClearOptions();
                selectionModeDropdown.AddOptions(new List<string>
                {
                    "Completely Random Mix",
                    "By Behavioral Topic",
                    "Choose Exact Questions"
                });
                selectionModeDropdown.value = (int)sessionManager.ActiveConfig.selectionMode;
                selectionModeDropdown.onValueChanged.AddListener(SetSelectionModeIndex);
            }

            if (topicDropdown != null)
            {
                topicDropdown.onValueChanged.AddListener(SelectSingleTopicByIndex);
            }

            if (startInterviewButton != null) startInterviewButton.onClick.AddListener(OnClickStartInterview);
            if (beginQuestionButton != null) beginQuestionButton.onClick.AddListener(OnClickBeginQuestion);
            if (nextQuestionButton != null) nextQuestionButton.onClick.AddListener(OnClickNextQuestion);
            if (replayAudioButton != null) replayAudioButton.onClick.AddListener(OnClickReplayQuestionAudio);
            if (endSessionButton != null) endSessionButton.onClick.AddListener(OnClickEndSession);

            if (interviewWentGoodButton != null) interviewWentGoodButton.onClick.AddListener(OnClickSubmitGoodOutcome);
            if (interviewWentBadButton != null) interviewWentBadButton.onClick.AddListener(OnClickSubmitBadOutcome);

            if (tryAgainFromBadButton != null) tryAgainFromBadButton.onClick.AddListener(OnClickReturnToSetup);
            if (practiceAgainFromGoodButton != null) practiceAgainFromGoodButton.onClick.AddListener(OnClickReturnToSetup);
        }

        private void PopulateTopicDropdown()
        {
            if (topicDropdown == null || sessionManager.Database == null) return;

            sessionManager.Database.EnsureLoaded();
            topicDropdown.ClearOptions();

            List<string> options = new List<string>(sessionManager.Database.Topics);
            topicDropdown.AddOptions(options);

            if (options.Count > 0 && sessionManager.ActiveConfig.selectedTopics.Count == 0)
            {
                sessionManager.ActiveConfig.selectedTopics.Add(options[0]);
            }
        }

        private void RefreshSetupLabels()
        {
            InterviewConfig cfg = sessionManager.ActiveConfig;

            if (questionCountValueText != null)
            {
                questionCountValueText.text = $"{cfg.numberOfQuestions} Questions";
            }

            if (selectedDurationText != null)
            {
                selectedDurationText.text = $"Duration: {cfg.DurationInMinutes} mins";
            }

            if (topicDropdown != null)
            {
                topicDropdown.gameObject.SetActive(cfg.selectionMode == QuestionSelectionMode.ByTopic);
            }
        }

        private void HandleStateChanged(InterviewSessionState state)
        {
            if (setupPanel != null) setupPanel.SetActive(state == InterviewSessionState.Setup);
            if (interviewPanel != null)
            {
                interviewPanel.SetActive(
                    state == InterviewSessionState.QuestionPresented ||
                    state == InterviewSessionState.Answering);
            }
            if (selfEvaluationPanel != null) selfEvaluationPanel.SetActive(state == InterviewSessionState.SelfEvaluation);
            if (goodOutcomePanel != null) goodOutcomePanel.SetActive(state == InterviewSessionState.OutcomeGood);
            if (badOutcomePanel != null) badOutcomePanel.SetActive(state == InterviewSessionState.OutcomeBad);

            RefreshActiveInterviewButtons();
        }

        private void RefreshActiveInterviewButtons()
        {
            if (beginQuestionButton != null)
            {
                beginQuestionButton.interactable = (sessionManager.CurrentState == InterviewSessionState.QuestionPresented);
            }
        }

        private void HandleQuestionPresented(QuestionItem question, int currentNumber, int totalPlanned)
        {
            if (questionProgressText != null)
            {
                questionProgressText.text = $"Question {currentNumber} of {totalPlanned}";
            }

            if (questionTopicText != null)
            {
                questionTopicText.text = question.topic;
            }

            if (questionPromptText != null)
            {
                questionPromptText.text = question.text;
            }

            RefreshActiveInterviewButtons();
        }

        private void HandleTimerUpdated(float remainingSeconds, float totalSeconds)
        {
            if (timerText != null)
            {
                timerText.text = InterviewSessionManager.FormatTimeMMSS(remainingSeconds);
            }
        }

        private void HandleSessionEnded(SessionEndReason reason, List<AnsweredQuestionRecord> records)
        {
            if (evaluationSummaryText != null)
            {
                string reasonLabel = reason switch
                {
                    SessionEndReason.TimeExpired => "Session Duration Reached (00:00)",
                    SessionEndReason.ManualUserEnd => "Ended Early by User",
                    _ => "All Configured Questions Completed"
                };

                int completedCount = 0;
                foreach (AnsweredQuestionRecord r in records)
                {
                    if (r.wasCompleted) completedCount++;
                }

                evaluationSummaryText.text =
                    $"Session Complete ({reasonLabel}) — Practiced {records.Count} question(s) ({completedCount} marked complete).";
            }

            if (rubricHintsDisplayText != null)
            {
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < records.Count; i++)
                {
                    QuestionItem q = records[i].question;
                    if (q == null) continue;

                    sb.AppendLine($"Q{i + 1} [{q.topic}]: {q.text}");
                    if (q.rubric_hints != null)
                    {
                        foreach (string hint in q.rubric_hints)
                        {
                            sb.AppendLine($"  • Rubric Hint: {hint}");
                        }
                    }
                    sb.AppendLine();
                }

                rubricHintsDisplayText.text = sb.ToString().TrimEnd();
            }
        }

        private void SyncSlidersAndSubmitEvaluation(bool feltGood)
        {
            SelfEvaluationManager eval = sessionManager.Evaluation;
            if (eval != null)
            {
                if (situationTaskSlider != null) eval.SetSituationAndTaskScore(Mathf.RoundToInt(situationTaskSlider.value));
                if (actionSpecificitySlider != null) eval.SetActionSpecificityScore(Mathf.RoundToInt(actionSpecificitySlider.value));
                if (resultImpactSlider != null) eval.SetResultAndImpactScore(Mathf.RoundToInt(resultImpactSlider.value));
                if (pacingConfidenceSlider != null) eval.SetPacingAndConfidenceScore(Mathf.RoundToInt(pacingConfidenceSlider.value));
                if (reflectionNotesInput != null) eval.ReflectionNotes = reflectionNotesInput.text;
            }

            sessionManager.SubmitSelfEvaluation(feltGood);
        }

        private void HandleBadOutcomeTips(List<string> tips)
        {
            if (feedbackTipsText == null || tips == null) return;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Key Areas to Focus on Next Attempt:");
            foreach (string tip in tips)
            {
                sb.AppendLine($"• {tip}");
            }
            feedbackTipsText.text = sb.ToString().TrimEnd();
        }
    }
}
