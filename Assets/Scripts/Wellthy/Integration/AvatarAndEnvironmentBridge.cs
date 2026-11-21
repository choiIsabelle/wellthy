using UnityEngine;
using Wellthy.Core;
using Wellthy.Data;

namespace Wellthy.Integration
{
    /// <summary>
    /// Optional plug-and-play bridge for connecting your teammate's Blender Interviewer Rig
    /// and 3D Unity Environments to InterviewSessionManager without modifying core logic scripts.
    /// </summary>
    public class AvatarAndEnvironmentBridge : MonoBehaviour
    {
        [Header("Core Session Manager")]
        [SerializeField] private InterviewSessionManager sessionManager;

        [Header("Teammate's Blender Avatar Rig (Optional)")]
        [SerializeField] private Animator interviewerAnimator;
        [SerializeField] private string isTalkingBoolParam = "IsTalking";
        [SerializeField] private string isListeningBoolParam = "IsListening";

        [Header("Teammate's Room Backdrops & Fancy Office (Optional)")]
        [Tooltip("Assign interview room backdrop root objects here (e.g., Startup Office, Boardroom, Minimalist Studio).")]
        [SerializeField] private GameObject[] interviewRoomBackdrops;

        [Tooltip("Assign the 'Fancy Office + Job Offer' root GameObject shown when the user selects 'Good' at the end.")]
        [SerializeField] private GameObject fancyOfficeWithJobOfferRoot;

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

        private void OnEnable()
        {
            if (sessionManager == null) return;

            sessionManager.onStateChanged.AddListener(HandleStateChanged);
            sessionManager.onRoomBackdropChanged.AddListener(ApplyRoomBackdrop);
            sessionManager.onInterviewerStartSpeaking.AddListener(HandleStartSpeaking);
            sessionManager.onInterviewerStopSpeaking.AddListener(HandleStopSpeaking);
            sessionManager.onUserBeginAnswering.AddListener(HandleUserBeginAnswering);
        }

        private void OnDisable()
        {
            if (sessionManager == null) return;

            sessionManager.onStateChanged.RemoveListener(HandleStateChanged);
            sessionManager.onRoomBackdropChanged.RemoveListener(ApplyRoomBackdrop);
            sessionManager.onInterviewerStartSpeaking.RemoveListener(HandleStartSpeaking);
            sessionManager.onInterviewerStopSpeaking.RemoveListener(HandleStopSpeaking);
            sessionManager.onUserBeginAnswering.RemoveListener(HandleUserBeginAnswering);
        }

        public void ApplyRoomBackdrop(int backdropIndex)
        {
            if (fancyOfficeWithJobOfferRoot != null)
            {
                fancyOfficeWithJobOfferRoot.SetActive(false);
            }

            if (interviewRoomBackdrops == null || interviewRoomBackdrops.Length == 0) return;

            int clamped = Mathf.Clamp(backdropIndex, 0, interviewRoomBackdrops.Length - 1);
            for (int i = 0; i < interviewRoomBackdrops.Length; i++)
            {
                if (interviewRoomBackdrops[i] != null)
                {
                    interviewRoomBackdrops[i].SetActive(i == clamped);
                }
            }
        }

        private void HandleStateChanged(InterviewSessionState state)
        {
            if (state == InterviewSessionState.OutcomeGood)
            {
                // Hide standard interview room backdrops and activate the Fancy Office + Job Offer
                if (interviewRoomBackdrops != null)
                {
                    foreach (GameObject room in interviewRoomBackdrops)
                    {
                        if (room != null) room.SetActive(false);
                    }
                }

                if (fancyOfficeWithJobOfferRoot != null)
                {
                    fancyOfficeWithJobOfferRoot.SetActive(true);
                }
            }
            else if (state == InterviewSessionState.Setup)
            {
                ApplyRoomBackdrop(sessionManager.ActiveConfig.roomBackdropIndex);
                SetAnimatorFlags(talking: false, listening: false);
            }
        }

        private void HandleStartSpeaking(AudioClip _)
        {
            SetAnimatorFlags(talking: true, listening: false);
        }

        private void HandleStopSpeaking()
        {
            bool isAnswering = sessionManager != null && sessionManager.CurrentState == InterviewSessionState.Answering;
            SetAnimatorFlags(talking: false, listening: isAnswering);
        }

        private void HandleUserBeginAnswering(QuestionItem _)
        {
            bool speaking = sessionManager != null && sessionManager.IsInterviewerSpeaking;
            SetAnimatorFlags(talking: speaking, listening: !speaking);
        }

        private void SetAnimatorFlags(bool talking, bool listening)
        {
            if (interviewerAnimator == null) return;

            if (!string.IsNullOrEmpty(isTalkingBoolParam))
            {
                interviewerAnimator.SetBool(isTalkingBoolParam, talking);
            }

            if (!string.IsNullOrEmpty(isListeningBoolParam))
            {
                interviewerAnimator.SetBool(isListeningBoolParam, listening);
            }
        }
    }
}
