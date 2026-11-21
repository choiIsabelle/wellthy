using System.Collections.Generic;
using UnityEngine;
using Wellthy.Config;
using Wellthy.Core;
using Wellthy.Data;
using Wellthy.Evaluation;

namespace Wellthy.UI
{
    /// <summary>
    /// Zero-setup interactive UI harness using Unity's built-in OnGUI.
    /// Drop this component onto ANY empty GameObject in an empty scene and press Play
    /// to test all Wellthy functionality immediately without building Canvas prefabs first.
    /// </summary>
    [RequireComponent(typeof(InterviewSessionManager))]
    public class WellthyImmediateTestUI : MonoBehaviour
    {
        [SerializeField] private bool showImmediateUI = true;
        [SerializeField] private InterviewSessionManager sessionManager;

        private Vector2 _topicScrollPos;
        private Vector2 _exactQuestionsScrollPos;
        private Vector2 _evalQuestionsScrollPos;
        private string _exactQuestionSearchFilter = string.Empty;

        private void Awake()
        {
            if (sessionManager == null)
            {
                sessionManager = GetComponent<InterviewSessionManager>();
            }
        }

        private void OnGUI()
        {
            if (!showImmediateUI || sessionManager == null) return;

            float width = Mathf.Min(860f, Screen.width - 32f);
            float height = Mathf.Min(680f, Screen.height - 32f);
            Rect windowRect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);

            GUI.Box(windowRect, GUIContent.none);
            GUILayout.BeginArea(new Rect(windowRect.x + 18f, windowRect.y + 14f, windowRect.width - 36f, windowRect.height - 28f));

            GUILayout.Label($"<b><size=18>Wellthy — Behavioral Interview Simulator ({sessionManager.CurrentState})</size></b>");
            GUILayout.Space(8f);

            switch (sessionManager.CurrentState)
            {
                case InterviewSessionState.Setup:
                    DrawSetupScreen();
                    break;
                case InterviewSessionState.QuestionPresented:
                case InterviewSessionState.Answering:
                    DrawActiveInterviewScreen();
                    break;
                case InterviewSessionState.SelfEvaluation:
                    DrawSelfEvaluationScreen();
                    break;
                case InterviewSessionState.OutcomeGood:
                    DrawGoodOutcomeScreen();
                    break;
                case InterviewSessionState.OutcomeBad:
                    DrawBadOutcomeScreen();
                    break;
            }

            GUILayout.EndArea();
        }

        private void DrawSetupScreen()
        {
            InterviewConfig cfg = sessionManager.ActiveConfig;
            QuestionDatabase db = sessionManager.Database;
            db.EnsureLoaded();

            // 1. Session Duration (5, 15, 45 mins)
            GUILayout.Label("<b>1. Select Session Duration:</b>");
            GUILayout.BeginHorizontal();
            DrawDurationToggleButton(cfg, SessionDurationPreset.FiveMinutes, "5 Minutes");
            DrawDurationToggleButton(cfg, SessionDurationPreset.FifteenMinutes, "15 Minutes");
            DrawDurationToggleButton(cfg, SessionDurationPreset.FortyFiveMinutes, "45 Minutes");
            GUILayout.EndHorizontal();

            GUILayout.Space(10f);

            // 2. Question Selection Mode
            GUILayout.Label("<b>2. Question Selection Mode:</b>");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(FormatSelectedLabel("Completely Random Mix", cfg.selectionMode == QuestionSelectionMode.RandomMix), GUILayout.Height(30f)))
            {
                cfg.selectionMode = QuestionSelectionMode.RandomMix;
            }
            if (GUILayout.Button(FormatSelectedLabel("Select Behavioral Topic(s)", cfg.selectionMode == QuestionSelectionMode.ByTopic), GUILayout.Height(30f)))
            {
                cfg.selectionMode = QuestionSelectionMode.ByTopic;
            }
            if (GUILayout.Button(FormatSelectedLabel("Choose Exact Questions", cfg.selectionMode == QuestionSelectionMode.ExactSelection), GUILayout.Height(30f)))
            {
                cfg.selectionMode = QuestionSelectionMode.ExactSelection;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(10f);

            // 3. Number of Questions
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>3. Max Questions in Session: {cfg.numberOfQuestions}</b>", GUILayout.Width(240f));
            if (GUILayout.Button("-1", GUILayout.Width(45f)) && cfg.numberOfQuestions > 1)
            {
                cfg.SetQuestionCount(cfg.numberOfQuestions - 1);
            }
            int sliderCount = Mathf.RoundToInt(GUILayout.HorizontalSlider(cfg.numberOfQuestions, 1f, 30f, GUILayout.Width(220f)));
            if (sliderCount != cfg.numberOfQuestions)
            {
                cfg.SetQuestionCount(sliderCount);
            }
            if (GUILayout.Button("+1", GUILayout.Width(45f)) && cfg.numberOfQuestions < 100)
            {
                cfg.SetQuestionCount(cfg.numberOfQuestions + 1);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(10f);

            // 4. Mode-specific selector (Topics or Exact Questions)
            if (cfg.selectionMode == QuestionSelectionMode.ByTopic)
            {
                GUILayout.Label($"<b>Select Behavioral Topic(s) ({cfg.selectedTopics.Count} selected):</b>");
                _topicScrollPos = GUILayout.BeginScrollView(_topicScrollPos, GUILayout.Height(220f));
                foreach (string topic in db.Topics)
                {
                    bool isSelected = cfg.selectedTopics.Contains(topic);
                    bool nextState = GUILayout.Toggle(isSelected, $"  {topic}");
                    if (nextState != isSelected)
                    {
                        cfg.ToggleTopic(topic);
                    }
                }
                GUILayout.EndScrollView();
            }
            else if (cfg.selectionMode == QuestionSelectionMode.ExactSelection)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b>Choose Exact Questions ({cfg.selectedQuestionIds.Count} picked):</b>", GUILayout.Width(280f));
                GUILayout.Label("Filter:", GUILayout.Width(45f));
                _exactQuestionSearchFilter = GUILayout.TextField(_exactQuestionSearchFilter, GUILayout.Width(220f));
                if (GUILayout.Button("Clear Picked", GUILayout.Width(100f)))
                {
                    cfg.selectedQuestionIds.Clear();
                }
                GUILayout.EndHorizontal();

                _exactQuestionsScrollPos = GUILayout.BeginScrollView(_exactQuestionsScrollPos, GUILayout.Height(210f));
                string filterLower = (_exactQuestionSearchFilter ?? string.Empty).Trim().ToLowerInvariant();

                foreach (QuestionItem q in db.AllQuestions)
                {
                    if (!string.IsNullOrEmpty(filterLower))
                    {
                        bool matches =
                            q.id.ToLowerInvariant().Contains(filterLower) ||
                            q.topic.ToLowerInvariant().Contains(filterLower) ||
                            q.text.ToLowerInvariant().Contains(filterLower);
                        if (!matches) continue;
                    }

                    bool isPicked = cfg.selectedQuestionIds.Contains(q.id);
                    bool toggled = GUILayout.Toggle(isPicked, $" [{q.id}] ({q.topic}) {q.text}");
                    if (toggled != isPicked)
                    {
                        cfg.ToggleExactQuestionId(q.id);
                        if (cfg.selectedQuestionIds.Count > 0)
                        {
                            cfg.SetQuestionCount(cfg.selectedQuestionIds.Count);
                        }
                    }
                }
                GUILayout.EndScrollView();
            }
            else
            {
                GUILayout.Box(
                    $"Completely Random Mix will shuffle all {db.AllQuestions.Count} behavioral questions across {db.Topics.Count} topics.\n" +
                    $"You can answer up to {cfg.numberOfQuestions} question(s) within your {cfg.DurationInMinutes}-minute timer, or end the session early at any time.",
                    GUILayout.Height(70f));
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button($"START INTERVIEW ({cfg.DurationInMinutes} mins · Up to {cfg.numberOfQuestions} Questions)", GUILayout.Height(46f)))
            {
                sessionManager.StartSession(cfg);
            }
        }

        private void DrawActiveInterviewScreen()
        {
            QuestionItem q = sessionManager.CurrentQuestion;

            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b><size=16>Remaining Session Time: {InterviewSessionManager.FormatTimeMMSS(sessionManager.RemainingTimeSeconds)}</size></b>");
            GUILayout.FlexibleSpace();
            GUILayout.Label($"<b>Question {sessionManager.CurrentQuestionNumber} of {sessionManager.TotalQuestionsInQueue}</b>");
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);

            if (q != null)
            {
                GUILayout.Label($"<b>Topic:</b> {q.topic}  |  <b>ID:</b> {q.id}  |  <b>Interviewer Voice:</b> {(sessionManager.IsInterviewerSpeaking ? "Speaking..." : "Listening")}");
                GUILayout.Space(6f);
                GUILayout.Box($"<size=15>{q.text}</size>", GUILayout.MinHeight(110f), GUILayout.ExpandWidth(true));
            }

            GUILayout.Space(10f);
            GUILayout.Label($"Current Question Answer Time: {InterviewSessionManager.FormatTimeMMSS(sessionManager.CurrentQuestionElapsedSeconds)}");

            GUILayout.Space(16f);
            GUILayout.BeginHorizontal();

            GUI.enabled = (sessionManager.CurrentState == InterviewSessionState.QuestionPresented);
            if (GUILayout.Button("Begin Answering Question", GUILayout.Height(42f)))
            {
                sessionManager.BeginAnsweringCurrentQuestion();
            }
            GUI.enabled = true;

            if (GUILayout.Button("Replay Question Audio", GUILayout.Height(42f)))
            {
                sessionManager.ReplayCurrentQuestionAudio();
            }

            bool hasMore = sessionManager.CurrentQuestionNumber < sessionManager.TotalQuestionsInQueue;
            string nextLabel = hasMore ? "Next Question ->" : "Complete Last Question ->";
            if (GUILayout.Button(nextLabel, GUILayout.Height(42f)))
            {
                sessionManager.CompleteCurrentQuestionAndAdvance();
            }

            GUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("End Session Early & Go to Self-Evaluation", GUILayout.Height(38f)))
            {
                sessionManager.EndSessionManually();
            }
        }

        private void DrawSelfEvaluationScreen()
        {
            SelfEvaluationManager eval = sessionManager.Evaluation;
            IReadOnlyList<AnsweredQuestionRecord> records = eval.SessionQuestions;

            GUILayout.Label($"<b>Session Ended ({sessionManager.LastEndReason}) — Answered {records.Count} Question(s)</b>");
            GUILayout.Label("Review the question-specific rubric points below and rate your performance using the STAR criteria:");

            GUILayout.Space(6f);

            _evalQuestionsScrollPos = GUILayout.BeginScrollView(_evalQuestionsScrollPos, GUILayout.Height(230f));
            for (int i = 0; i < records.Count; i++)
            {
                AnsweredQuestionRecord rec = records[i];
                if (rec?.question == null) continue;

                GUILayout.BeginVertical("box");
                GUILayout.Label($"<b>Q{i + 1} [{rec.question.topic}] ({InterviewSessionManager.FormatTimeMMSS(rec.timeSpentSeconds)}):</b> {rec.question.text}");
                if (rec.question.rubric_hints != null)
                {
                    for (int h = 0; h < rec.question.rubric_hints.Length; h++)
                    {
                        bool checkedNow = rec.rubricHintsChecked != null && h < rec.rubricHintsChecked.Length && rec.rubricHintsChecked[h];
                        bool nextChecked = GUILayout.Toggle(checkedNow, $"  Rubric Check: {rec.question.rubric_hints[h]}");
                        if (nextChecked != checkedNow)
                        {
                            eval.SetRubricHintChecked(i, h, nextChecked);
                        }
                    }
                }
                GUILayout.EndVertical();
            }
            GUILayout.EndScrollView();

            GUILayout.Space(8f);
            GUILayout.Label("<b>Overall Behavioral Rubric Self-Rating (1 = Needs Work, 5 = Strong):</b>");

            DrawStarScoreRow("Situation & Task Clarity:", eval.CurrentScores.situationAndTaskClarity, eval.SetSituationAndTaskScore);
            DrawStarScoreRow("Action Specificity ('I' vs 'We'):", eval.CurrentScores.actionSpecificity, eval.SetActionSpecificityScore);
            DrawStarScoreRow("Result & Measurable Impact:", eval.CurrentScores.resultAndImpact, eval.SetResultAndImpactScore);
            DrawStarScoreRow("Pacing & Vocal Confidence:", eval.CurrentScores.pacingAndConfidence, eval.SetPacingAndConfidenceScore);

            GUILayout.FlexibleSpace();
            GUILayout.Label("<b>Final Reflection: How do you feel the interview went?</b>");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("It Went GOOD (Enter Fancy Office + Job Offer)", GUILayout.Height(44f)))
            {
                sessionManager.SubmitSelfEvaluation(true);
            }
            if (GUILayout.Button("It Went BAD (View Feedback Tips & Retry)", GUILayout.Height(44f)))
            {
                sessionManager.SubmitSelfEvaluation(false);
            }
            GUILayout.EndHorizontal();
        }

        private void DrawGoodOutcomeScreen()
        {
            GUILayout.Space(20f);
            GUILayout.Label("<b><size=20>Congratulations — You Got the Job Offer!</size></b>");
            GUILayout.Space(10f);
            GUILayout.Box(
                "You selected that your interview went GOOD.\n\n" +
                "In VR, this state triggers the transition into your new Fancy Office with a Job Offer on the desk " +
                "(via InterviewSessionManager -> SelfEvaluationManager.onGoodOutcomeSelected).",
                GUILayout.Height(110f));

            if (sessionManager.Evaluation.LastResult != null)
            {
                GUILayout.Space(10f);
                GUILayout.Label($"<b>Your Average STAR Self-Rating:</b> {sessionManager.Evaluation.LastResult.starScores.AverageScore:F1} / 5.0");
            }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Practice Another Session", GUILayout.Height(44f)))
            {
                sessionManager.ReturnToSetup();
            }
        }

        private void DrawBadOutcomeScreen()
        {
            GUILayout.Space(10f);
            GUILayout.Label("<b><size=18>Keep Going — Personalized Feedback Tips for Your Next Attempt</size></b>");
            GUILayout.Space(8f);

            SelfEvaluationResult result = sessionManager.Evaluation.LastResult;
            if (result != null && result.generatedFeedbackTips != null)
            {
                foreach (string tip in result.generatedFeedbackTips)
                {
                    GUILayout.Box($"• {tip}", GUILayout.ExpandWidth(true));
                    GUILayout.Space(4f);
                }
            }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Try Again (Return to Setup)", GUILayout.Height(44f)))
            {
                sessionManager.ReturnToSetup();
            }
        }

        private static void DrawDurationToggleButton(InterviewConfig cfg, SessionDurationPreset preset, string label)
        {
            bool isSelected = cfg.durationPreset == preset;
            if (GUILayout.Button(FormatSelectedLabel(label, isSelected), GUILayout.Height(34f)))
            {
                cfg.durationPreset = preset;
            }
        }

        private static void DrawStarScoreRow(string label, int currentScore, System.Action<int> onScoreSelected)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(240f));
            for (int score = 1; score <= 5; score++)
            {
                string btnLabel = score == currentScore ? $"[{score}]" : score.ToString();
                if (GUILayout.Button(btnLabel, GUILayout.Width(48f)))
                {
                    onScoreSelected?.Invoke(score);
                }
            }
            GUILayout.EndHorizontal();
        }

        private static string FormatSelectedLabel(string text, bool isSelected)
        {
            return isSelected ? $"● {text}" : text;
        }
    }
}
