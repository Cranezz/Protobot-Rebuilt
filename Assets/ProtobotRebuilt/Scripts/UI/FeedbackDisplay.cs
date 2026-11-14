using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Protobot.UI {
    public class FeedbackDisplay : MonoBehaviour {
        [CacheComponent] private UIMovement movement;

        [Header("UI Reference")]
        public Text displayText;
        public Image displayImage;

        [Header("Sprites")]
        public Sprite alertSprite;
        public Sprite successSprite;

        public enum DisplayType {alert, success}

        [Header("Current Data")]
        public bool showing;
        public float duration = 5;
        public DisplayType curDisplayType;
        public string curDisplayString;
        private float timer;
        private RectTransform rectTransform;

        void Start() {
            rectTransform = GetComponent<RectTransform>();
            timer = 0;
        }

        void Update() {
            if (showing) {
                displayText.text = curDisplayString;
                rectTransform.sizeDelta =
                    new Vector2(displayText.preferredWidth + 40, rectTransform.sizeDelta.y);
                
                if (curDisplayType == DisplayType.alert) {
                    displayImage.sprite = alertSprite;
                }
                else if (curDisplayType == DisplayType.success) {
                    displayImage.sprite = successSprite;
                }
                movement.SetActivePos();
                timer += Time.deltaTime;

                if (timer >= duration)
                    showing = false;
            }
            else {
                timer = 0;
                movement.ResetPos();
            }
        }

        // V2: the user wants few notifications: confirmations of ordinary actions ("Cut 2\"", "Painted…") aren't shown;
        // refusals and problems (alerts) are, and the few successes worth telling (a file saved or exported, a score)
        // go through Interface.Announce, which sets this for its message.
        public static bool important;

        public void Show(string stringToDisplay, DisplayType type) {
            if (type == DisplayType.success && !important) return;
            curDisplayType = type;
            curDisplayString = stringToDisplay;
            showing = true;
        }

        public void ShowSuccess(string stringToDisplay) {
            Show(stringToDisplay, DisplayType.success);
        }

        public void ShowAlert(string stringToDisplay) {
            Show(stringToDisplay, DisplayType.alert);
        }
    }
}