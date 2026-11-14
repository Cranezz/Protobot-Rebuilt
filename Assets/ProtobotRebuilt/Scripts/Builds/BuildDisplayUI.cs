using UnityEngine;
using UnityEngine.UI;
using Protobot.Builds;
using System;
using System.Collections;
using TMPro;

namespace Protobot.UI {
    public class BuildDisplayUI : MonoBehaviour {
        public BuildsManager buildsManager;
        public TMP_Text buildNameText;
        public string buildPath;

        public void UpdateUI(string path, BuildsManager _buildsManager) {
            buildPath = path;
            buildNameText.text = BuildsManager.PathToFileName(path);
            // V2: a long name stays on one line, cut short (long ones ran over the next row).
            buildNameText.textWrappingMode = TextWrappingModes.NoWrap;
            buildNameText.overflowMode = TextOverflowModes.Ellipsis;
            buildsManager = _buildsManager;
        }

        public void LoadBuild() {
            var build = BuildsManager.ParsePath(buildPath);

            if (build != null)
                buildsManager.AttemptLoad(build, buildPath);
            else
                StartCoroutine(DisplayLoadError());
        }
        
        IEnumerator DisplayLoadError() {
            buildNameText.text = "File path not found...";
            buildNameText.color = new Color(0.9f,0.3f,0.3f,0);
            yield return new WaitForSeconds(1);
            BuildsListUI.OnLoadError?.Invoke();
        }
    }
}