using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.Video;
using MelonLoader;

namespace VRFSCam
{
    public class SceneSequenceMod : MelonMod
    {
        // ─── CONFIG ─────────────────────────────────────────────────────────────
        private const string NextSceneName = "StartUp"; // Must be in Build Settings
        private AssetBundle introassets;
        private AssetBundle introscene;
        // ────────────────────────────────────────────────────────────────────────

        public override void OnLateInitializeMelon()
        {
            StartSceneSequence();
        }

        public override void OnInitializeMelon()
        {
            base.OnInitializeMelon();
            introassets = AssetBundle.LoadFromMemory(VRFSCam.AssetBundles.introassets);
            introscene = AssetBundle.LoadFromMemory(VRFSCam.AssetBundles.introscene);
        }
        private void StartSceneSequence()
        {
            MelonCoroutines.Start(SequenceCoroutine());
        }

        private IEnumerator SequenceCoroutine()
        {

            var paths = introscene.GetAllScenePaths();
            if (paths.Length == 0)
            {
                MelonLogger.Error("No scenes found in scene bundle.");
                yield break;
            }
            string sceneName = "Intro";
            MelonLogger.Msg($"Loading {sceneName}..");
            yield return SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);

            // 4) Find/assign your VideoPlayer’s clip from the assets bundle, then wait for it to finish
            var vp = UnityEngine.Object.FindObjectOfType<VideoPlayer>();
            if (vp != null)
            {
                if (vp.clip == null)
                {
                    var clips = introassets.LoadAllAssets<VideoClip>();
                    if (clips.Length > 0)
                        vp.clip = clips[0];
                }

                vp.Play();
                MelonLogger.Msg("Waiting for intro to finish...");
                double waitTime = vp.clip != null ? vp.clip.length : 2.0;
                yield return new WaitForSeconds((float)waitTime);
            }
            else
            {
                MelonLogger.Warning("No VideoPlayer found in scene.");
            }

            // 5) Finally, load your next scene (from Build Settings)
            MelonLogger.Msg($"Loading game...");
            yield return SceneManager.LoadSceneAsync(NextSceneName, LoadSceneMode.Single);

            // Cleanup
            introscene.Unload(false);
            introassets.Unload(false);
            MelonLogger.Msg("Sequence complete.");
        }
    }
}