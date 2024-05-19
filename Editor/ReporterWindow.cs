#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JetBrains.Annotations;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
#if REPORT_ASSET_BUNDLE_SIZE_VRCSDK3A
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;
#endif
#if REPORT_ASSET_BUNDLE_SIZE_NDMF
using nadena.dev.modular_avatar.core.editor;
#endif

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
            
            var b = new Button(() => ClickEvent(buildAvatarToCheck, callPreBuiltHook, compressedSize, decompressedSize, preserveBuiltBundles));
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
                var invalid = cb.newValue == null;
                
                invalid = invalid || ExtractBuildTarget(cb.newValue) == null;
                absentAvatarError.style.display = invalid ? DisplayStyle.Flex : DisplayStyle.None;
                b.SetEnabled(!invalid);
            });

            var runNDMF = new Toggle("Call Non-Destructive Modular Framework?")
            {
                tooltip = "Call Non-Destructive Modular Framework?"
            };
            #if REPORT_ASSET_BUNDLE_SIZE_VRCSDK3A
            runNDMF.SetEnabled(false);
            runNDMF.tooltip += "\n(NDMF is called via VRCSDK hook)";
            callPreBuiltHook.RegisterValueChangedCallback(evt =>
            {
                runNDMF.value = evt.newValue;
            });
            #endif
            
            rootVisualElement.Add(buildAvatarToCheck);
            rootVisualElement.Add(compressedSize);
            rootVisualElement.Add(decompressedSize);
            rootVisualElement.Add(callPreBuiltHook);
            rootVisualElement.Add(runNDMF);
            rootVisualElement.Add(absentAvatarError);
            b.Add(new Label("ビルド"));
            rootVisualElement.Add(b);
        }

        [CanBeNull]
        private static GameObject ExtractBuildTarget(Object buildCandidate)
        {
#if REPORT_ASSET_BUNDLE_SIZE_VRCSDK3A
            if (buildCandidate is VRCAvatarDescriptor desc)
            {
                return desc.gameObject;
            } else 
#endif
            if (buildCandidate is GameObject go)
            {
                return go;
            }
            
            return null;
        }
        private static void ClickEvent(ObjectField buildAvatarToCheck, Toggle callPreBuiltHook, TextField compressedSize, TextField decompressedSize, Toggle preserveBuiltBundles)
        {
            GameObject original = ExtractBuildTarget(buildAvatarToCheck.value);
            if (original == null)
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

            #if REPORT_ASSET_BUNDLE_SIZE_VRCFURY
            // remark: https://github.com/KisaragiEffective/ReportAssetBundleSize/issues/17
            ad.name += " (Clone)";
            #endif
            
            if (callPreBuiltHook.value)
            {
                #if REPORT_ASSET_BUNDLE_SIZE_VRCSDK3A
                if (!VRCBuildPipelineCallbacks.OnPreprocessAvatar(ad))
                {
                    throw new Exception("some of VRCSDK callback reports failure");
                }
                #elif REPORT_ASSET_BUNDLE_SIZE_NDMF
                AvatarProcessor.ProcessAvatar(ad);
                #endif
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

            Build("Compressed", BuildAssetBundleOptions.ChunkBasedCompression, in compressedSize, localPath, temporaryDirectory);
            // TODO: だいぶ頭悪い可能性がある
            Build("Decompressed", BuildAssetBundleOptions.UncompressedAssetBundle, in decompressedSize, localPath, temporaryDirectory);
            if (!preserveBuiltBundles.value)
            {
                var di = new DirectoryInfo(temporaryDirectory);
                // metaファイルを消さないと文句を言ってくる
                File.Delete(temporaryDirectory + "/../" + di.Name + ".meta");
                di.Delete(true);
            }
            // シーンから除去しないとシーンにたまる
            GameObject.DestroyImmediate(ad);
        }

        private static void Build(string assetBundleName, BuildAssetBundleOptions opts, in TextField write, string localPath, string temporaryDirectory)
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
#endif
