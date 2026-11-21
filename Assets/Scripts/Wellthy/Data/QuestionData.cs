using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wellthy.Data
{
    /// <summary>
    /// Matches a single question entry in audio_gen/questions.json.
    /// Field names match the JSON keys so Unity's built-in JsonUtility can deserialize directly.
    /// </summary>
    [Serializable]
    public class QuestionItem
    {
        public string id;
        public string type;
        public string topic;
        public string text;
        public string[] rubric_hints;

        /// <summary>
        /// Returns the expected filename (without extension) for this question's pre-generated MP3 clip.
        /// </summary>
        public string AudioClipName => id;
    }

    /// <summary>
    /// Root wrapper matching {"questions": [...]} in audio_gen/questions.json.
    /// </summary>
    [Serializable]
    public class QuestionBankRoot
    {
        public List<QuestionItem> questions = new List<QuestionItem>();
    }

    /// <summary>
    /// Tracks a question that was presented/answered during an active interview session,
    /// along with which rubric hints the user checked off during self-evaluation.
    /// </summary>
    [Serializable]
    public class AnsweredQuestionRecord
    {
        public QuestionItem question;
        public float timeSpentSeconds;
        public bool wasCompleted;
        public bool[] rubricHintsChecked;

        public AnsweredQuestionRecord(QuestionItem question)
        {
            this.question = question;
            this.timeSpentSeconds = 0f;
            this.wasCompleted = false;
            int hintCount = (question != null && question.rubric_hints != null)
                ? question.rubric_hints.Length
                : 0;
            this.rubricHintsChecked = new bool[hintCount];
        }

        public int CheckedHintCount
        {
            get
            {
                if (rubricHintsChecked == null) return 0;
                int count = 0;
                for (int i = 0; i < rubricHintsChecked.Length; i++)
                {
                    if (rubricHintsChecked[i]) count++;
                }
                return count;
            }
        }
    }
}
