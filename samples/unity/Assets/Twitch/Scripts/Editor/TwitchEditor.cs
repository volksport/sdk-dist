using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using Twitch.Broadcast;
using Twitch.Chat;

namespace Twitch
{
	public class TwitchEditor : Editor
	{
		#region Grabbing SDK Binaries
		
		private struct Entry
		{
			public Entry(string i, string o){ inputPath = i; outputPath = o; }
			public string inputPath;
			public string outputPath;
		}
		
		private static Entry[] s_Entries = new Entry[]
		{
			new Entry(@"../../lib/twitchsdk.dll", @"Assets/Plugins/{UNITY_PLUGIN_DIR}/twitchsdk.dll"),
			new Entry(@"../../lib/twitchsdk.pdb", @"Assets/Plugins/{UNITY_PLUGIN_DIR}/twitchsdk.pdb"),
			new Entry(@"../../bindings/csharp/wrapper/bin/{WRAPPER_PLATFORM}/{WRAPPER_CONFIG}/TwitchSdkWrapper.dll", @"Assets/Plugins/TwitchSdkWrapper.dll"),
			new Entry(@"../../bindings/csharp/wrapper/bin/{WRAPPER_PLATFORM}/{WRAPPER_CONFIG}/TwitchSdkWrapper.pdb", @"Assets/Plugins/TwitchSdkWrapper.pdb"),
			
			new Entry(@"../../ffmpeg/bin/{FFMPEG_DIR}/avutil-ttv-51.dll",  @"Assets/Plugins/{UNITY_PLUGIN_DIR}/avutil-ttv-51.dll"),
			new Entry(@"../../ffmpeg/bin/{FFMPEG_DIR}/swresample-ttv-0.dll",  @"Assets/Plugins/{UNITY_PLUGIN_DIR}/swresample-ttv-0.dll"),

			new Entry(@"../../libmp3lame/bin/{LAME_DIR}/libmp3lame-ttv.dll",  @"Assets/Plugins/{UNITY_PLUGIN_DIR}/libmp3lame-ttv.dll"),

			new Entry(@"../../libcurl/bin/{CURL_DIR}/libcurl-ttv.dll",  @"Assets/Plugins/{UNITY_PLUGIN_DIR}/libcurl-ttv.dll"), 
			new Entry(@"../../libcurl/bin/{CURL_DIR}/ssleay32-ttv.dll",  @"Assets/Plugins/{UNITY_PLUGIN_DIR}/ssleay32-ttv.dll"),
			new Entry(@"../../libcurl/bin/{CURL_DIR}/libeay32-ttv.dll",  @"Assets/Plugins/{UNITY_PLUGIN_DIR}/libeay32-ttv.dll"),

			new Entry(@"../../intel/bin/{INTEL_DIR}/libmfxsw{INTEL_PLATFORM}.dll",  @"Assets/Plugins/{UNITY_PLUGIN_DIR}/libmfxsw{INTEL_PLATFORM}.dll"),
		};

        [MenuItem("Twitch/Grab Binaries/Debug x86")]
        protected static void GrabDebugX86Binaries()
        {
            GrabBinaries("x86", true);
        }

        [MenuItem("Twitch/Grab Binaries/Release x86")]
        protected static void GrabReleaseX86Binaries()
        {
            GrabBinaries("x86", false);
        }

        [MenuItem("Twitch/Grab Binaries/Debug x64")]
        protected static void GrabDebugX64Binaries()
        {
            GrabBinaries("x64", true);
        }

        [MenuItem("Twitch/Grab Binaries/Release x64")]
        protected static void GrabReleaseX64Binaries()
        {
            GrabBinaries("x64", false);
        }
		
		protected static void GrabBinaries(string platform, bool debug)
		{
			string TWITCHSDK_CONFIG = debug ? "debug" : "release";
			string WRAPPER_CONFIG = debug ? "Debug" : "Release";

            string UNITY_PLUGIN_DIR = platform == "x86" ? "x86" : "x86_64";
            string FFMPEG_DIR = platform == "x86" ? "win32" : "x64";
            string LAME_DIR = platform == "x86" ? "win32" : "x64";
            string CURL_DIR = platform == "x86" ? "win32" : "x64";
            string INTEL_DIR = platform == "x86" ? "win32" : "x64";
            string INTEL_PLATFORM = platform == "x86" ? "32" : "64";
            string WRAPPER_PLATFORM = "AnyCPU";

			foreach (Entry e in s_Entries)
			{
				string inputPath = e.inputPath;
                inputPath = inputPath.Replace("{PLATFORM}", platform);
				inputPath = inputPath.Replace("{TWITCHSDK_CONFIG}", TWITCHSDK_CONFIG);
                inputPath = inputPath.Replace("{WRAPPER_CONFIG}", WRAPPER_CONFIG);
                inputPath = inputPath.Replace("{WRAPPER_PLATFORM}", WRAPPER_PLATFORM);
                inputPath = inputPath.Replace("{FFMPEG_DIR}", FFMPEG_DIR);
                inputPath = inputPath.Replace("{LAME_DIR}", LAME_DIR);
                inputPath = inputPath.Replace("{CURL_DIR}", CURL_DIR);
                inputPath = inputPath.Replace("{INTEL_DIR}", INTEL_DIR);
                inputPath = inputPath.Replace("{INTEL_PLATFORM}", INTEL_PLATFORM);
				
				string outputPath = e.outputPath;
                outputPath = outputPath.Replace("{PLATFORM}", platform);
				outputPath = outputPath.Replace("{TWITCHSDK_CONFIG}", TWITCHSDK_CONFIG);
				outputPath = outputPath.Replace("{WRAPPER_CONFIG}", WRAPPER_CONFIG);
                outputPath = outputPath.Replace("{UNITY_PLUGIN_DIR}", UNITY_PLUGIN_DIR);
                outputPath = outputPath.Replace("{INTEL_PLATFORM}", INTEL_PLATFORM);
				
				if (File.Exists(outputPath))
				{
					File.Delete(outputPath);
				}
				File.Copy(inputPath, outputPath);
				
				Debug.Log(string.Format("Copied {0} -> {1}", inputPath, outputPath));
			}
		}
		
		#endregion
		
		#region Filling in Credentials

        [MenuItem("Twitch/Populate My Credentials")]
        protected static void PopulateMyCredentials()
        {
            // TODO: fill in credentials here that you enter often
            PopulateCredentials("", "", "", "");
        }

        [MenuItem("Twitch/Clear Credentials")]
        protected static void ClearCredentials()
        {
            PopulateCredentials("", "", "", "");
        }

        protected static void PopulateCredentials(string username, string password, string clientid, string clientsecret)
        {
            Object[] arr = Object.FindObjectsOfType(typeof(UnityBroadcastController));
            foreach (Object obj in arr)
            {
                UnityBroadcastController sc = (UnityBroadcastController)obj;
                sc.ClientId = clientid;
                sc.ClientSecret = clientsecret;

                EditorUtility.SetDirty(sc);
            }

            arr = Object.FindObjectsOfType(typeof(BroadcastGUI));
            foreach (Object obj in arr)
            {
                BroadcastGUI gui = (BroadcastGUI)obj;
                gui.UserName = username;
                gui.Password = password;

                EditorUtility.SetDirty(gui);
            }

            arr = Object.FindObjectsOfType(typeof(UnityChatController));
            foreach (Object obj in arr)
            {
                UnityChatController cc = (UnityChatController)obj;
                cc.ClientId = clientid;
                cc.ClientSecret = clientsecret;
                cc.UserName = username;

                EditorUtility.SetDirty(cc);
            }

            arr = Object.FindObjectsOfType(typeof(ChatGUI));
            foreach (Object obj in arr)
            {
                ChatGUI gui = (ChatGUI)obj;
                gui.UserName = username;
                gui.Password = password;
                gui.Channel = username;

                EditorUtility.SetDirty(gui);
            }
        }
		
		#endregion

        #region Export Package

        [MenuItem("Twitch/Export Package")]
        protected static void ExportPackage()
        {
            string outputPath = "C:/Drew/Repositories/twitchbins/TwitchUnity.unitypackage";

            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            List<string> inputPaths = new List<string>();

            // export Assets/Plugins DLLs
            string[] files = Directory.GetFiles("Assets/Plugins", "*.dll", SearchOption.AllDirectories);
            foreach (string path in files)
            {
                FileInfo file = new FileInfo(path);

                // skip files
                if (file.Name.ToLower() != "twitchsdk.dll" &&
                    file.Name.ToLower() != "twitchsdkwrapper.dll")
                {
                    continue;
                }

                string assetPath = GetAssetPath(path);

                inputPaths.Add(assetPath);
            }

            // export Assets/Plugins/twitchsdk.bundle
            files = Directory.GetFiles("Assets/Plugins/twitchsdk.bundle", "*.*", SearchOption.AllDirectories);
            foreach (string path in files)
            {
                FileInfo file = new FileInfo(path);

                // skip files
                if (file.Extension.ToLower() == ".meta")
                {
                    continue;
                }

                string assetPath = GetAssetPath(path);

                inputPaths.Add(assetPath);
            }

            // export Assets/Twitch
            files = Directory.GetFiles("Assets/Twitch", "*.*", SearchOption.AllDirectories);
            foreach (string path in files)
            {
                FileInfo file = new FileInfo(path);

                // skip files
                if (file.Extension.ToLower() == ".meta" ||
                    file.Name.ToLower() == "twitcheditor.cs")
                {
                    continue;
                }

                string assetPath = GetAssetPath(path);

                inputPaths.Add(assetPath);
            }

            // log the export
            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            sb.Append("Exporting package to ").Append(outputPath).AppendLine();
            foreach (string path in inputPaths)
            {
                sb.Append("    ").Append(path).AppendLine();
            }

            Debug.Log(sb.ToString());

            AssetDatabase.ExportPackage(inputPaths.ToArray(), outputPath);
        }

        protected static string GetAssetPath(string fullpath)
        {
            int index = fullpath.ToLower().IndexOf("assets");
            if (index < 0)
            {
                return fullpath;
            }
            else
            {
                return fullpath.Substring(index);
            }
        }

        #endregion
    }
}
