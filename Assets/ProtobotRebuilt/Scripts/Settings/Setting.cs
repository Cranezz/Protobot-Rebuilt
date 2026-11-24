using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using System;

namespace Protobot {
    [Serializable]
    public class Setting : MonoBehaviour {
        public string currentValue;

        public object RealValue {
            get {
                // V2: numbers with a dot whatever the PC's language (a comma-decimal PC read "0.0033" as 33: the camera
                // flew), and a comma from such a PC's old saves still reads as a decimal point.
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                if (int.TryParse(currentValue, System.Globalization.NumberStyles.Integer, inv, out int intResult))
                    return intResult;
                else if (float.TryParse(currentValue.Replace(',', '.'), System.Globalization.NumberStyles.Float, inv, out float floatResult))
                    return floatResult;
                else if (bool.TryParse(currentValue, out bool boolResult))
                    return boolResult;

                return currentValue;
            }
        }

        private string SavedValue => PlayerPrefs.GetString(name);

        public IntEvent OnUpdateIntValue;
        public BoolEvent OnUpdateBoolValue;
        public FloatEvent OnUpdateFloatValue;

        public IntEvent OnInitIntValue;
        public BoolEvent OnInitBoolValue;
        public FloatEvent OnInitFloatValue;

        public void Start() {
            InitValue();
        }

        public void InitValue() {
            if (SavedValue.Length != 0) {
                SetValue(SavedValue);
            }

            Type type = RealValue.GetType();

            if (type == typeof(int)) {
                OnInitIntValue.Invoke((int)RealValue);
            }
            else if (type == typeof(float)) {
                OnInitFloatValue.Invoke((float)RealValue);
            }
            else if (type == typeof(bool)) {
                OnInitBoolValue.Invoke((bool)RealValue);
            }
        }

        public void SaveValue() {
            PlayerPrefs.SetString(name, currentValue);
        }

        public void SetValue(string newValue) {
            currentValue = newValue;

            Type type = RealValue.GetType();

            if (type == typeof(int)) {
                OnUpdateIntValue.Invoke((int)RealValue);
            }
            else if (type == typeof(float)) {
                OnUpdateFloatValue.Invoke((float)RealValue);
            }
            else if (type == typeof(bool)) {
                OnUpdateBoolValue.Invoke((bool)RealValue);
            }

            SaveValue();
        }

        public void SetValue(int newValue) => SetValue(newValue.ToString());
        public void SetValue(float newValue) => SetValue(newValue.ToString(System.Globalization.CultureInfo.InvariantCulture));   // V2: always a dot
        public void SetValue(bool newValue) => SetValue(newValue.ToString());
    }
}
