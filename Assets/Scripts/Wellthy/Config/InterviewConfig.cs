using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wellthy.Config
{
    /// <summary>
    /// Supported interview session duration presets (5, 15, and 45 minutes).
    /// </summary>
    public enum SessionDurationPreset
    {
        FiveMinutes = 5,
        FifteenMinutes = 15,
        FortyFiveMinutes = 45
    }

    /// <summary>
    /// Controls how behavioral questions are chosen for the session.
    /// </summary>
    public enum QuestionSelectionMode
    {
        /// <summary>
        /// Completely random mix pulled from all behavioral questions.
        /// </summary>
        RandomMix = 0,

        /// <summary>
        /// Randomly sampled from one or more user-selected behavioral topics
        /// (e.g., "Teamwork & Collaboration", "Debugging, Incidents & Problem Solving").
        /// </summary>
        ByTopic = 1,

        /// <summary>
        /// User hand-picks the exact questions they want to practice.
        /// </summary>
        ExactSelection = 2
    }

    /// <summary>
    /// Holds all user-selected configuration parameters before starting an interview session.
    /// </summary>
    [Serializable]
    public class InterviewConfig
    {
        [Header("Question Count & Duration")]
        [Tooltip("Maximum number of questions to ask in this session (used in RandomMix and ByTopic modes).")]
        [Range(1, 100)]
        public int numberOfQuestions = 5;

        [Tooltip("Session duration preset: 5, 15, or 45 minutes.")]
        public SessionDurationPreset durationPreset = SessionDurationPreset.FifteenMinutes;

        [Header("Question Selection")]
        [Tooltip("Whether to pick a completely random mix, filter by behavioral topic, or choose exact questions.")]
        public QuestionSelectionMode selectionMode = QuestionSelectionMode.RandomMix;

        [Tooltip("Selected behavioral topics when selectionMode == ByTopic.")]
        public List<string> selectedTopics = new List<string>();

        [Tooltip("Selected exact question IDs (e.g., 'beh_001', 'beh_014') when selectionMode == ExactSelection.")]
        public List<string> selectedQuestionIds = new List<string>();

        [Tooltip("Whether to shuffle the selected questions when using ExactSelection mode.")]
        public bool shuffleExactSelection = false;

        [Header("Optional Environment Setting")]
        [Tooltip("Room backdrop index for Place Illusion (0 = Startup Office, 1 = Boardroom, 2 = Minimalist Studio).")]
        public int roomBackdropIndex = 0;

        /// <summary>
        /// Total session duration in seconds (300s for 5m, 900s for 15m, 2700s for 45m).
        /// </summary>
        public float DurationInSeconds => (int)durationPreset * 60f;

        /// <summary>
        /// Total session duration in minutes (5, 15, or 45).
        /// </summary>
        public int DurationInMinutes => (int)durationPreset;

        public void SetDurationMinutes(int minutes)
        {
            switch (minutes)
            {
                case 5:
                    durationPreset = SessionDurationPreset.FiveMinutes;
                    break;
                case 15:
                    durationPreset = SessionDurationPreset.FifteenMinutes;
                    break;
                case 45:
                    durationPreset = SessionDurationPreset.FortyFiveMinutes;
                    break;
                default:
                    Debug.LogWarning($"[InterviewConfig] Unsupported preset {minutes}m; defaulting to 15 minutes.");
                    durationPreset = SessionDurationPreset.FifteenMinutes;
                    break;
            }
        }

        public void SetQuestionCount(int count)
        {
            numberOfQuestions = Mathf.Clamp(count, 1, 100);
        }

        public void ToggleTopic(string topic)
        {
            if (string.IsNullOrWhiteSpace(topic)) return;
            if (selectedTopics.Contains(topic))
            {
                selectedTopics.Remove(topic);
            }
            else
            {
                selectedTopics.Add(topic);
            }
        }

        public void ToggleExactQuestionId(string questionId)
        {
            if (string.IsNullOrWhiteSpace(questionId)) return;
            if (selectedQuestionIds.Contains(questionId))
            {
                selectedQuestionIds.Remove(questionId);
            }
            else
            {
                selectedQuestionIds.Add(questionId);
            }
        }

        /// <summary>
        /// Creates a shallow copy of the configuration for safe runtime use.
        /// </summary>
        public InterviewConfig Clone()
        {
            return new InterviewConfig
            {
                numberOfQuestions = this.numberOfQuestions,
                durationPreset = this.durationPreset,
                selectionMode = this.selectionMode,
                selectedTopics = new List<string>(this.selectedTopics),
                selectedQuestionIds = new List<string>(this.selectedQuestionIds),
                shuffleExactSelection = this.shuffleExactSelection,
                roomBackdropIndex = this.roomBackdropIndex
            };
        }
    }
}
