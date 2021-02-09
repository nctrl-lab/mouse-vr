using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
#if UNITY_EDITOR
using UnityEditor.Callbacks;
#endif

namespace Janelia
{
    public static class Timeout
    {
#if UNITY_EDITOR
        [PostProcessBuildAttribute(SessionParameters.POST_PROCESS_BUILD_ORDER + 1)]
        public static void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject)
        {
            Debug.Log("Janelia.Timeout.OnPostprocessBuild: " + pathToBuiltProject);

            SessionParameters.AddFloatParameter("timeoutSecs", 0);
        }
#endif

        [RuntimeInitializeOnLoadMethod]
        private static async Task OnRuntimeMethodLoadAsync()
        {
            // Note that this function is marked `RuntimeInitializeOnLoadMethod` witn
            // no additional argument.  Thus, it should get executed after 
            // `SessionParameters.OnRuntimeMethodLoad`, where the parameters file
            // gets loaded to set the value for thie `GetFloatParameters` call.
            double timeout = SessionParameters.GetFloatParameter("timeoutSecs");
            if (timeout > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(timeout));
                Application.Quit();
            }
        }
    }
}
