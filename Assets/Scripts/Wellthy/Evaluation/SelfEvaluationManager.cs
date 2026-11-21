using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Wellthy.Data;

namespace Wellthy.Evaluation
{
    /// <summary>
    /// Core STAR behavioral rubric dimensions that the user scores themselves on at the end of a session.
    /// </summary>
    [Serializable]
    public class StarRubricScores
    {
        [Range(1, 5)] public int situationAndTaskClarity = 3;
        [Range(1, 5)] public int actionSpecificity = 3;
        [Range(1, 5)] public int resultAndImpact = 3;
        [Range(1, 5)] public int pacingAndConfidence = 3;

        public float AverageScore =>
            (situationAndTaskClarity + actionSpecificity + resultAndImpact + pacingAndConfidence) / 4f;
    }

    /// <summary>
    /// Complete summary of a user's self-evaluation at the end of an interview session.
    /// </summary>
    [Serializable]
    public class SelfEvaluationResult
    {
        public bool feltInterviewWentGood;
        public StarRubricScores starScores = new StarRubricScores();
        public List<AnsweredQuestionRecord> reviewedQuestions = new List<AnsweredQuestionRecord>();
        public string reflectionNotes = string.Empty;
        public List<string> generatedFeedbackTips = new List<string>();
    }

    /// <summary>
    /// Manages the end-of-session rubric interpretation, self-evaluation checklist,
    /// and the Good (Fancy Office + Job Offer) vs. Bad (Feedback Tips + Try Again) outcome branching.
    /// </summary>
    public class SelfEvaluationManager : MonoBehaviour
    {
        [Header("Current Self-Evaluation State")]
        [SerializeField] private StarRubricScores currentScores = new StarRubricScores();
        [SerializeField] [TextArea(2, 5)] private string reflectionNotes = string.Empty;

        [Header("Outcome Events (Hook Teammate's Fancy Office / UI Popups Here)")]
        public UnityEvent<SelfEvaluationResult> onEvaluationCompleted = new UnityEvent<SelfEvaluationResult>();
        public UnityEvent onGoodOutcomeSelected = new UnityEvent();
        public UnityEvent<List<string>> onBadOutcomeSelected = new UnityEvent<List<string>>();

        private List<AnsweredQuestionRecord> _sessionQuestions = new List<AnsweredQuestionRecord>();

        public StarRubricScores CurrentScores => currentScores;
        public IReadOnlyList<AnsweredQuestionRecord> SessionQuestions => _sessionQuestions;
        public SelfEvaluationResult LastResult { get; private set; }

        public string ReflectionNotes
        {
            get => reflectionNotes;
            set => reflectionNotes = value ?? string.Empty;
        }

        /// <summary>
        /// Initializes a fresh self-evaluation sheet for the questions answered during the session.
        /// </summary>
        public void BeginEvaluation(List<AnsweredQuestionRecord> answeredQuestions)
        {
            _sessionQuestions = answeredQuestions != null
                ? new List<AnsweredQuestionRecord>(answeredQuestions)
                : new List<AnsweredQuestionRecord>();

            currentScores = new StarRubricScores();
            reflectionNotes = string.Empty;
            LastResult = null;
        }

        /// <summary>
        /// Toggles whether the user satisfied a specific rubric hint on one of their answered questions.
        /// </summary>
        public void SetRubricHintChecked(int questionIndex, int hintIndex, bool isChecked)
        {
            if (questionIndex < 0 || questionIndex >= _sessionQuestions.Count) return;
            AnsweredQuestionRecord record = _sessionQuestions[questionIndex];
            if (record?.rubricHintsChecked == null) return;
            if (hintIndex < 0 || hintIndex >= record.rubricHintsChecked.Length) return;

            record.rubricHintsChecked[hintIndex] = isChecked;
        }

        public void SetSituationAndTaskScore(int score) => currentScores.situationAndTaskClarity = Mathf.Clamp(score, 1, 5);
        public void SetActionSpecificityScore(int score) => currentScores.actionSpecificity = Mathf.Clamp(score, 1, 5);
        public void SetResultAndImpactScore(int score) => currentScores.resultAndImpact = Mathf.Clamp(score, 1, 5);
        public void SetPacingAndConfidenceScore(int score) => currentScores.pacingAndConfidence = Mathf.Clamp(score, 1, 5);

        /// <summary>
        /// Submits the self-evaluation with the user's reflection on whether the interview went Good (true) or Bad (false).
        /// - If Good: triggers onGoodOutcomeSelected (transitions to Fancy Office + Job Offer).
        /// - If Bad: generates actionable feedback tips from unchecked rubric hints and low STAR scores, and triggers onBadOutcomeSelected.
        /// </summary>
        public SelfEvaluationResult SubmitEvaluation(bool feltInterviewWentGood)
        {
            List<string> tips = GenerateFeedbackTips();

            LastResult = new SelfEvaluationResult
            {
                feltInterviewWentGood = feltInterviewWentGood,
                starScores = new StarRubricScores
                {
                    situationAndTaskClarity = currentScores.situationAndTaskClarity,
                    actionSpecificity = currentScores.actionSpecificity,
                    resultAndImpact = currentScores.resultAndImpact,
                    pacingAndConfidence = currentScores.pacingAndConfidence
                },
                reviewedQuestions = new List<AnsweredQuestionRecord>(_sessionQuestions),
                reflectionNotes = this.reflectionNotes,
                generatedFeedbackTips = tips
            };

            onEvaluationCompleted?.Invoke(LastResult);

            if (feltInterviewWentGood)
            {
                onGoodOutcomeSelected?.Invoke();
            }
            else
            {
                onBadOutcomeSelected?.Invoke(tips);
            }

            return LastResult;
        }

        /// <summary>
        /// Builds targeted interview coaching tips based on the user's STAR scores and any missed question rubric points.
        /// </summary>
        public List<string> GenerateFeedbackTips()
        {
            List<string> tips = new List<string>();

            if (currentScores.situationAndTaskClarity <= 3)
            {
                tips.Add("Situation & Task (STAR): Spend the first 20-30 seconds setting concise context so the interviewer immediately understands the problem and stakes.");
            }

            if (currentScores.actionSpecificity <= 3)
            {
                tips.Add("Action Specificity (STAR): Use 'I' instead of 'we' when describing your technical steps so the interviewer knows your exact personal contribution.");
            }

            if (currentScores.resultAndImpact <= 3)
            {
                tips.Add("Result & Impact (STAR): Always close your answer with concrete outcomes, metrics, or what you learned from the experience.");
            }

            if (currentScores.pacingAndConfidence <= 3)
            {
                tips.Add("Pacing & Delivery: Pause for 2-3 seconds after hearing the question to structure your thoughts before speaking, and aim for 1.5 to 2.5 minutes per behavioral answer.");
            }

            // Include up to 3 specific missed rubric hints from questions they practiced
            int addedQuestionHints = 0;
            foreach (AnsweredQuestionRecord record in _sessionQuestions)
            {
                if (record?.question?.rubric_hints == null || record.rubricHintsChecked == null) continue;

                for (int i = 0; i < record.question.rubric_hints.Length && i < record.rubricHintsChecked.Length; i++)
                {
                    if (!record.rubricHintsChecked[i])
                    {
                        tips.Add($"On \"{Truncate(record.question.text, 55)}\", try to highlight: {record.question.rubric_hints[i]}.");
                        addedQuestionHints++;
                        break;
                    }
                }

                if (addedQuestionHints >= 3) break;
            }

            if (tips.Count == 0)
            {
                tips.Add("Strong structure across the rubric! Next run, try increasing the session duration or practicing unfamiliar behavioral topics to build even more consistency.");
            }

            return tips;
        }

        private static string Truncate(string value, int maxChars)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxChars) return value;
            return value.Substring(0, maxChars).TrimEnd() + "...";
        }
    }
}
