#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
#if REPORT_ASSET_BUNDLE_SIZE_VRCSDK3A
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;
#endif
using Object = UnityEngine.Object;

namespace ReportAssetBundleSize.Editor
{
    public class ReporterWindow : EditorWindow
    {
        [MenuItem("Tools/ReportAssetBundleSize")]
        private static void ShowEditorWindow()
        {
            EditorWindow wnd = GetWindow<ReporterWindow>();
            wnd.titleContent = new GUIContent("Reporter");
        }

        private void CreateGUI()
        {
            var callPreBuiltHook = new Toggle("Call VRCSDK hooks")
                { tooltip = "do you want NDMF or like-wise tools to run?", value = false };
            #if !REPORT_ASSET_BUNDLE_SIZE_VRCSDK3A
            callPreBuiltHook.SetEnabled(false);
            callPreBuiltHook.tooltip += " (This requires VRCSDK installation)";
            #endif
            var buildAvatarToCheck = new ObjectField("Game Object to check") { objectType = typeof(GameObject) };
            buildAvatarToCheck.RegisterValueChangedCallback(cb =>
            {
                Debug.Log($"previous: {cb.previousValue}");
                var current = cb.newValue;
                Debug.Log($"new: {current}");
                {
#if REPORT_ASSET_BUNDLE_SIZE_VRCSDK3A
                    if ((buildAvatarToCheck.value as GameObject)?.TryGetComponent<VRCAvatarDescriptor>(out _) ?? false)
                    {
                        // TODO: distinct whether the GO has VRC-AD
                        callPreBuiltHook.style.display = DisplayStyle.Flex;
                    }
                    else
#endif
                    {
                        callPreBuiltHook.style.display = DisplayStyle.None;
                    }
                }
            });
            var compressedSize = new TextField("Compressed size") { value = "???", isReadOnly = true };
            var decompressedSize = new TextField("Decompressed size") { value = "???", isReadOnly = true };
            var preserveBuiltBundles = new Toggle("Preserve built bundles")
            {
                tooltip = "Preserves build artifact if checked. Otherwise it will be deleted", value = false,
            };
            rootVisualElement.Add(preserveBuiltBundles);
            
            var b = new Button(ClickEvent);
            b.SetEnabled(false);
            
            var absentAvatarError = new HelpBox("調べるためにはアバターをセットしてください", HelpBoxMessageType.Error)
            {
                style =
                {
                    display = DisplayStyle.Flex
                }
            };
            buildAvatarToCheck.RegisterValueChangedCallback(cb =>
            {
                var absent = cb.newValue == null;
                absentAvatarError.style.display = absent ? DisplayStyle.Flex : DisplayStyle.None;
                b.SetEnabled(!absent);
            });

            rootVisualElement.Add(buildAvatarToCheck);
            rootVisualElement.Add(compressedSize);
            rootVisualElement.Add(decompressedSize);
            rootVisualElement.Add(callPreBuiltHook);
            // TODO: toggle visibility
            rootVisualElement.Add(absentAvatarError);
            b.Add(new Label("ビルド"));
            rootVisualElement.Add(b);
            return;

            void ClickEvent()
            {
                GameObject original;
                #if REPORT_ASSET_BUNDLE_SIZE_VRCSDK3A
                if (buildAvatarToCheck.value is VRCAvatarDescriptor desc)
                {
                    original = desc.gameObject;
                } else 
                #endif
                if (buildAvatarToCheck.value is GameObject go)
                {
                    original = go;
                }
                else
                {
                    throw new Exception("VRCAvatarDescriptor or GameObject is expected");
                }
                #if REPORT_ASSET_BUNDLE_SIZE_NDMF
                // クローンすることでNDMFプラグインによって元々のゲームオブジェクトが破壊されることを防ぐ
                var ad = GameObject.Instantiate(original);
                #else
                // ReSharper disable once InlineTemporaryVariable
                var ad = original;
                #endif
                
                if (callPreBuiltHook.value)
                {
                    if (!VRCBuildPipelineCallbacks.OnPreprocessAvatar(ad))
                    {
                        throw new Exception("some of VRCSDK callback reports failure");
                    }
                }
                
                if (buildAvatarToCheck.value is null)
                {
                    throw new Exception("please set Avatar to check");
                }

                Debug.Log("build");
                var randomGUID = Guid.NewGuid().ToString();
                var baseDir = "Assets/ReportAssetBundleSize__Gen";
                if (!Directory.Exists(baseDir))
                {
                    Directory.CreateDirectory(baseDir);
                }
                
                var temporaryDirectory = baseDir + "/" + randomGUID;
                if (!Directory.Exists(temporaryDirectory))
                {
                    Directory.CreateDirectory(temporaryDirectory);
                }
                
                // パスが`**/*.prefab`の形式じゃないと例外を吐く。知るかよ！
                var localPath = AssetDatabase.GenerateUniqueAssetPath(temporaryDirectory + "/" + ad.gameObject.name + ".prefab");
                {
                    PrefabUtility.SaveAsPrefabAsset(ad.gameObject, localPath, out var s);
                    if (!s)
                    {
                        Debug.LogError("persistence failed!");
                        return;
                    }
                }

                Build("Compressed", BuildAssetBundleOptions.ChunkBasedCompression, in compressedSize);
                // TODO: だいぶ頭悪い可能性がある
                Build("Decompressed", BuildAssetBundleOptions.UncompressedAssetBundle, in decompressedSize);
                if (!preserveBuiltBundles.value)
                {
                    var di = new DirectoryInfo(temporaryDirectory);
                    // metaファイルを消さないと文句を言ってくる
                    File.Delete(temporaryDirectory + "/../" + di.Name + ".meta");
                    di.Delete(true);
                }
                return;

                void Build(string assetBundleName, BuildAssetBundleOptions opts, in TextField write)
                {
                    var bundlesToBuild = new List<AssetBundleBuild> { new() { assetNames = new[] { localPath }, assetBundleName = assetBundleName } };

                    // TODO: 多分BuildTargetは関係ないと思う。思いたい。だれかよろしく！ｗ
                    var compressedManifest = BuildPipeline.BuildAssetBundles(
                        temporaryDirectory, bundlesToBuild.ToArray(), opts, BuildTarget.StandaloneWindows64
                    );

                    if (compressedManifest == null)
                    {
                        Debug.LogError($"Build ({assetBundleName}) failed, see Console and Editor log for details");
                        return;
                    }

                    Debug.Log($"Build finished ({assetBundleName})");
                    var builtAssetBundleRelativePath = compressedManifest.GetAllAssetBundles()!.Single();
                    var projectRelativePath = temporaryDirectory + "/" + builtAssetBundleRelativePath;
                    var len = new FileInfo(projectRelativePath).Length;
                    write.value = $"{len} bytes ({len / 1024.0 / 1024.0:F2} MiB / {len / 1000.0 / 1000.0:F2} MB)";
                }
            }
        }
    }
}
#endif
