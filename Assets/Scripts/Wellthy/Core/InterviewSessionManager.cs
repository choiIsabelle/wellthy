using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Wellthy.Config;
using Wellthy.Data;
using Wellthy.Evaluation;

namespace Wellthy.Core
{
    /// <summary>
    /// High-level state of the Wellthy VR interview simulator.
    /// </summary>
    public enum InterviewSessionState
    {
        Setup = 0,
        QuestionPresented = 1,
        Answering = 2,
        SelfEvaluation = 3,
        OutcomeGood = 4,
        OutcomeBad = 5
    }

    /// <summary>
    /// Why the active interview phase ended and transitioned into Self-Evaluation.
    /// </summary>
    public enum SessionEndReason
    {
        None = 0,
        AllQuestionsCompleted = 1,
        TimeExpired = 2,
        ManualUserEnd = 3
    }

    /// <summary>
    /// Central state machine and controller for Wellthy.
    /// Completely decoupled from the 3D environment and Blender rig via UnityEvents and C# events.
    /// </summary>
    public class InterviewSessionManager : MonoBehaviour
    {
        [Header("Core Dependencies")]
        [SerializeField] private QuestionDatabase questionDatabase;
        [SerializeField] private SelfEvaluationManager evaluationManager;

        [Tooltip("Optional: Assign the interviewer avatar's head AudioSource here. If null, a local AudioSource is created automatically.")]
        [SerializeField] private AudioSource interviewerAudioSource;

        [Header("Active Configuration")]
        [SerializeField] private InterviewConfig activeConfig = new InterviewConfig();

        [Tooltip("If true, the session countdown timer runs continuously during both QuestionPresented and Answering states.")]
        [SerializeField] private bool countTimeDuringQuestionPrompt = true;

        [Header("Lifecycle Events (For Teammate's VR Scene, Avatar & UI)")]
        public UnityEvent<InterviewSessionState> onStateChanged = new UnityEvent<InterviewSessionState>();
        public UnityEvent<int> onRoomBackdropChanged = new UnityEvent<int>();
        public UnityEvent<QuestionItem, int, int> onQuestionPresented = new UnityEvent<QuestionItem, int, int>();
        public UnityEvent<AudioClip> onInterviewerStartSpeaking = new UnityEvent<AudioClip>();
        public UnityEvent onInterviewerStopSpeaking = new UnityEvent();
        public UnityEvent<QuestionItem> onUserBeginAnswering = new UnityEvent<QuestionItem>();
        public UnityEvent<float, float> onTimerUpdated = new UnityEvent<float, float>();
        public UnityEvent<SessionEndReason, List<AnsweredQuestionRecord>> onSessionEnded = new UnityEvent<SessionEndReason, List<AnsweredQuestionRecord>>();

        private readonly List<QuestionItem> _sessionQueue = new List<QuestionItem>();
        private readonly List<AnsweredQuestionRecord> _answeredRecords = new List<AnsweredQuestionRecord>();

        private int _currentQuestionIndex = -1;
        private AnsweredQuestionRecord _currentRecord;
        private bool _wasAudioPlayingLastFrame;

        public InterviewSessionState CurrentState { get; private set; } = InterviewSessionState.Setup;
        public SessionEndReason LastEndReason { get; private set; } = SessionEndReason.None;

        public InterviewConfig ActiveConfig => activeConfig;
        public QuestionDatabase Database => questionDatabase;
        public SelfEvaluationManager Evaluation => evaluationManager;

        public IReadOnlyList<QuestionItem> SessionQueue => _sessionQueue;
        public IReadOnlyList<AnsweredQuestionRecord> AnsweredRecords => _answeredRecords;

        public int CurrentQuestionIndex => _currentQuestionIndex;
        public int CurrentQuestionNumber => _currentQuestionIndex + 1;
        public int TotalQuestionsInQueue => _sessionQueue.Count;

        public QuestionItem CurrentQuestion =>
            (_currentQuestionIndex >= 0 && _currentQuestionIndex < _sessionQueue.Count)
                ? _sessionQueue[_currentQuestionIndex]
                : null;

        public float TotalDurationSeconds { get; private set; }
        public float RemainingTimeSeconds { get; private set; }
        public float CurrentQuestionElapsedSeconds => _currentRecord != null ? _currentRecord.timeSpentSeconds : 0f;
        public bool IsInterviewerSpeaking => interviewerAudioSource != null && interviewerAudioSource.isPlaying;

        private void Awake()
        {
            if (questionDatabase == null)
            {
                questionDatabase = GetComponent<QuestionDatabase>();
                if (questionDatabase == null)
                {
                    questionDatabase = gameObject.AddComponent<QuestionDatabase>();
                }
            }

            if (evaluationManager == null)
            {
                evaluationManager = GetComponent<SelfEvaluationManager>();
                if (evaluationManager == null)
                {
                    evaluationManager = gameObject.AddComponent<SelfEvaluationManager>();
                }
            }

            if (interviewerAudioSource == null)
            {
                interviewerAudioSource = GetComponent<AudioSource>();
                if (interviewerAudioSource == null)
                {
                    interviewerAudioSource = gameObject.AddComponent<AudioSource>();
                    interviewerAudioSource.playOnAwake = false;
                }
            }

            evaluationManager.onGoodOutcomeSelected.AddListener(HandleGoodOutcome);
            evaluationManager.onBadOutcomeSelected.AddListener(HandleBadOutcome);
        }

        private void OnDestroy()
        {
            if (evaluationManager != null)
            {
                evaluationManager.onGoodOutcomeSelected.RemoveListener(HandleGoodOutcome);
                evaluationManager.onBadOutcomeSelected.RemoveListener(HandleBadOutcome);
            }
        }

        private void Update()
        {
            // Track when the interviewer finishes speaking the question audio clip
            if (_wasAudioPlayingLastFrame && !IsInterviewerSpeaking)
            {
                _wasAudioPlayingLastFrame = false;
                onInterviewerStopSpeaking?.Invoke();
            }

            bool isInterviewActive =
                CurrentState == InterviewSessionState.Answering ||
                (countTimeDuringQuestionPrompt && CurrentState == InterviewSessionState.QuestionPresented);

            if (!isInterviewActive) return;

            float dt = Time.deltaTime;
            RemainingTimeSeconds = Mathf.Max(0f, RemainingTimeSeconds - dt);

            if (_currentRecord != null && CurrentState == InterviewSessionState.Answering)
            {
                _currentRecord.timeSpentSeconds += dt;
            }

            onTimerUpdated?.Invoke(RemainingTimeSeconds, TotalDurationSeconds);

            if (RemainingTimeSeconds <= 0f)
            {
                FinishSessionInternal(SessionEndReason.TimeExpired);
            }
        }

        /// <summary>
        /// Starts a new interview session using the currently configured InterviewConfig.
        /// </summary>
        public void StartSession()
        {
            StartSession(activeConfig);
        }

        /// <summary>
        /// Starts a new interview session with the provided configuration.
        /// Builds the question queue, initializes the 5/15/45-minute countdown timer, and presents Question #1.
        /// </summary>
        public void StartSession(InterviewConfig config)
        {
            if (config != null)
            {
                activeConfig = config.Clone();
            }

            questionDatabase.EnsureLoaded();
            List<QuestionItem> builtQueue = questionDatabase.BuildSessionQueue(activeConfig);

            _sessionQueue.Clear();
            _sessionQueue.AddRange(builtQueue);
            _answeredRecords.Clear();
            _currentRecord = null;
            _currentQuestionIndex = -1;
            LastEndReason = SessionEndReason.None;

            if (_sessionQueue.Count == 0)
            {
                Debug.LogError("[InterviewSessionManager] Cannot start session because no questions were loaded.");
                return;
            }

            TotalDurationSeconds = activeConfig.DurationInSeconds;
            RemainingTimeSeconds = TotalDurationSeconds;

            onRoomBackdropChanged?.Invoke(activeConfig.roomBackdropIndex);
            onTimerUpdated?.Invoke(RemainingTimeSeconds, TotalDurationSeconds);

            PresentNextQuestion();
        }

        /// <summary>
        /// Called when the user clicks "Begin Question" to start answering the presented question aloud.
        /// </summary>
        public void BeginAnsweringCurrentQuestion()
        {
            if (CurrentState != InterviewSessionState.QuestionPresented || CurrentQuestion == null)
            {
                return;
            }

            SetState(InterviewSessionState.Answering);
            onUserBeginAnswering?.Invoke(CurrentQuestion);
        }

        /// <summary>
        /// Marks the current question as completed and advances to the next question if time and questions remain;
        /// otherwise ends the session and opens the Self-Evaluation screen.
        /// </summary>
        public void CompleteCurrentQuestionAndAdvance()
        {
            if (CurrentState != InterviewSessionState.QuestionPresented &&
                CurrentState != InterviewSessionState.Answering)
            {
                return;
            }

            if (_currentRecord != null)
            {
                _currentRecord.wasCompleted = true;
            }

            StopInterviewerAudio();

            bool hasMoreQuestions = (_currentQuestionIndex + 1) < _sessionQueue.Count;
            bool hasTimeRemaining = RemainingTimeSeconds > 0f;

            if (hasMoreQuestions && hasTimeRemaining)
            {
                PresentNextQuestion();
            }
            else
            {
                SessionEndReason reason = hasTimeRemaining
                    ? SessionEndReason.AllQuestionsCompleted
                    : SessionEndReason.TimeExpired;
                FinishSessionInternal(reason);
            }
        }

        /// <summary>
        /// Allows the user to manually end the interview session at any time and proceed straight to Self-Evaluation.
        /// </summary>
        public void EndSessionManually()
        {
            if (CurrentState != InterviewSessionState.QuestionPresented &&
                CurrentState != InterviewSessionState.Answering)
            {
                return;
            }

            FinishSessionInternal(SessionEndReason.ManualUserEnd);
        }

        /// <summary>
        /// Replays the spoken audio clip for the currently active question.
        /// </summary>
        public void ReplayCurrentQuestionAudio()
        {
            if (CurrentQuestion == null) return;
            PlayQuestionAudio(CurrentQuestion.id);
        }

        /// <summary>
        /// Submits the user's self-evaluation and transitions to either OutcomeGood (Fancy Office + Job Offer)
        /// or OutcomeBad (Feedback Tips + Try Again).
        /// </summary>
        public SelfEvaluationResult SubmitSelfEvaluation(bool feltInterviewWentGood)
        {
            if (CurrentState != InterviewSessionState.SelfEvaluation)
            {
                Debug.LogWarning("[InterviewSessionManager] SubmitSelfEvaluation called outside SelfEvaluation state.");
            }

            return evaluationManager.SubmitEvaluation(feltInterviewWentGood);
        }

        /// <summary>
        /// Resets back to the Setup screen so the user can reconfigure or retry.
        /// </summary>
        public void ReturnToSetup()
        {
            StopInterviewerAudio();
            _currentQuestionIndex = -1;
            _currentRecord = null;
            SetState(InterviewSessionState.Setup);
        }

        /// <summary>
        /// Formats a time value in seconds as MM:SS (e.g., "14:35").
        /// </summary>
        public static string FormatTimeMMSS(float timeInSeconds)
        {
            int clamped = Mathf.Max(0, Mathf.CeilToInt(timeInSeconds));
            int minutes = clamped / 60;
            int seconds = clamped % 60;
            return $"{minutes:00}:{seconds:00}";
        }

        private void PresentNextQuestion()
        {
            StopInterviewerAudio();

            _currentQuestionIndex++;
            QuestionItem nextQuestion = CurrentQuestion;
            if (nextQuestion == null)
            {
                FinishSessionInternal(SessionEndReason.AllQuestionsCompleted);
                return;
            }

            _currentRecord = new AnsweredQuestionRecord(nextQuestion);
            _answeredRecords.Add(_currentRecord);

            SetState(InterviewSessionState.QuestionPresented);
            onQuestionPresented?.Invoke(nextQuestion, CurrentQuestionNumber, TotalQuestionsInQueue);

            PlayQuestionAudio(nextQuestion.id);
        }

        private void PlayQuestionAudio(string questionId)
        {
            StopInterviewerAudio();

            questionDatabase.LoadQuestionAudio(questionId, clip =>
            {
                // Ensure the user hasn't already moved past this question while the clip loaded
                if (CurrentQuestion == null || !string.Equals(CurrentQuestion.id, questionId, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (clip != null && interviewerAudioSource != null)
                {
                    interviewerAudioSource.clip = clip;
                    interviewerAudioSource.Play();
                    _wasAudioPlayingLastFrame = true;
                    onInterviewerStartSpeaking?.Invoke(clip);
                }
            });
        }

        private void StopInterviewerAudio()
        {
            if (interviewerAudioSource != null && interviewerAudioSource.isPlaying)
            {
                interviewerAudioSource.Stop();
            }

            if (_wasAudioPlayingLastFrame)
            {
                _wasAudioPlayingLastFrame = false;
                onInterviewerStopSpeaking?.Invoke();
            }
        }

        private void FinishSessionInternal(SessionEndReason reason)
        {
            StopInterviewerAudio();
            LastEndReason = reason;

            evaluationManager.BeginEvaluation(_answeredRecords);
            SetState(InterviewSessionState.SelfEvaluation);

            onSessionEnded?.Invoke(reason, new List<AnsweredQuestionRecord>(_answeredRecords));
        }

        private void HandleGoodOutcome()
        {
            SetState(InterviewSessionState.OutcomeGood);
        }

        private void HandleBadOutcome(List<string> _)
        {
            SetState(InterviewSessionState.OutcomeBad);
        }

        private void SetState(InterviewSessionState nextState)
        {
            if (CurrentState == nextState) return;
            CurrentState = nextState;
            onStateChanged?.Invoke(CurrentState);
        }
    }
}
