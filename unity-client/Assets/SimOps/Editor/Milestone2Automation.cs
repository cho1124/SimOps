using System;
using System.IO;
using SimOps.Game.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace SimOps.Unity.Editor
{
    public static class Milestone2Automation
    {
        private const string ScenePath = "Assets/SimOps/Scenes/SimOpsArena.unity";
        private const string GoldenHash = "c50ea84e374db937ec1dd17ea94428b60afdb169b4d64dd5eeec64128fa2fa78";

        [MenuItem("SimOps/Automation/Create Bootstrap Scene")]
        public static void CreateBootstrapScene()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath) ?? "Assets/SimOps/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var controller = new GameObject("SimOpsGameController");
            controller.AddComponent<SimOpsGameController>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            ApplyPlayerSettings();
            AssetDatabase.SaveAssets();
            Debug.Log($"SIMOPS_SCENE_CREATED path={ScenePath}");
        }

        [MenuItem("SimOps/Automation/Verify Golden Run")]
        public static void VerifyGoldenRun()
        {
            var config = GameConfig.CreateBaseline();
            var scoreRule = ScoreRule.CreateBaseline();
            var context = new RunContext(
                config.GameVersion,
                config.Checksum,
                scoreRule.Version,
                scoreRule.Checksum,
                42UL);
            var simulation = new GameSimulation(config, scoreRule);
            var observation = simulation.Reset(context);

            while (observation.Phase != RunPhase.Terminal)
            {
                var action = SelectAction(observation);
                var step = simulation.Apply(action);
                if (!step.Accepted)
                {
                    throw new InvalidOperationException($"Golden policy action rejected: {step.RejectionCode}");
                }

                observation = step.Observation;
            }

            var result = simulation.GetCanonicalResult();
            if (!string.Equals(result.ResultHash, GoldenHash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Golden hash mismatch. expected={GoldenHash} actual={result.ResultHash}");
            }

            Debug.Log(
                $"SIMOPS_GOLDEN_PASS hash={result.ResultHash} score={result.FinalScore} " +
                $"outcome={result.Outcome} actions={simulation.ActionLog.Count}");
        }

        [MenuItem("SimOps/Automation/Build Windows Development")]
        public static void BuildWindowsDevelopment()
        {
            EnsureScene();
            var outputPath = GetArtifactPath("windows", "SimOps.exe");
            Build(
                new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = outputPath,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.Development | BuildOptions.AllowDebugging,
                },
                "Windows");
        }

        [MenuItem("SimOps/Automation/Build Android Development")]
        public static void BuildAndroidDevelopment()
        {
            EnsureScene();
            EditorUserBuildSettings.buildAppBundle = false;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            var outputPath = GetArtifactPath("android", "SimOps.apk");
            Build(
                new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = outputPath,
                    target = BuildTarget.Android,
                    options = BuildOptions.Development | BuildOptions.AllowDebugging,
                },
                "Android");
        }

        private static void EnsureScene()
        {
            if (!File.Exists(ScenePath))
            {
                CreateBootstrapScene();
            }
            else
            {
                ApplyPlayerSettings();
            }
        }

        private static void ApplyPlayerSettings()
        {
            EnsurePanelSettings();
            PlayerSettings.companyName = "SimOps Lab";
            PlayerSettings.productName = "SimOps Arena";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.insecureHttpOption = InsecureHttpOption.DevelopmentOnly;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "com.simops.arena");
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.simops.arena");
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
        }

        private static void EnsurePanelSettings()
        {
            const string path = "Assets/SimOps/Resources/SimOpsPanelSettings.asset";
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panel, path);
            }
            panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Assets/SimOps/Resources/SimOpsTheme.tss");
            if (panel.themeStyleSheet == null) throw new InvalidOperationException("Runtime theme asset is missing.");
            EditorUtility.SetDirty(panel);
            AssetDatabase.SaveAssets();
        }

        private static void Build(BuildPlayerOptions options, string platform)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(options.locationPathName) ?? string.Empty);
            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"{platform} build failed: {report.summary.result}, errors={report.summary.totalErrors}");
            }

            var notices = Path.Combine(Path.GetDirectoryName(options.locationPathName), "licenses", "NotoSansKR");
            Directory.CreateDirectory(notices);
            foreach (var file in new[] { "NOTICE.txt", "OFL.txt" })
                File.Copy(Path.Combine(Application.dataPath, "SimOps", "Resources", "Fonts", file), Path.Combine(notices, file), true);

            Debug.Log(
                $"SIMOPS_BUILD_PASS platform={platform} output={options.locationPathName} " +
                $"bytes={report.summary.totalSize}");
        }

        private static string GetArtifactPath(string platform, string fileName)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("Unity project root was not found.");
            var repositoryRoot = Directory.GetParent(projectRoot)?.FullName
                ?? throw new InvalidOperationException("Repository root was not found.");
            return Path.Combine(repositoryRoot, "artifacts", "unity", platform, fileName);
        }

        private static GameAction SelectAction(GameObservation observation)
        {
            if (observation.Phase == RunPhase.RewardChoice)
            {
                return new GameAction(
                    observation.NextActionSequence,
                    GameActionType.ChooseReward,
                    observation.OfferedRewardIds[0]);
            }

            if (observation.Player.CurrentHealth * 3 <= observation.Player.MaxHealth &&
                Contains(observation, GameActionType.UseItem))
            {
                return new GameAction(observation.NextActionSequence, GameActionType.UseItem);
            }

            if (observation.Enemy?.Intent == EnemyIntentType.HeavyAttack &&
                Contains(observation, GameActionType.Guard))
            {
                return new GameAction(observation.NextActionSequence, GameActionType.Guard);
            }

            if (Contains(observation, GameActionType.Technique))
            {
                return new GameAction(observation.NextActionSequence, GameActionType.Technique);
            }

            if (Contains(observation, GameActionType.Strike))
            {
                return new GameAction(observation.NextActionSequence, GameActionType.Strike);
            }

            return new GameAction(observation.NextActionSequence, GameActionType.EndTurn);
        }

        private static bool Contains(GameObservation observation, GameActionType actionType)
        {
            for (var index = 0; index < observation.ValidActionTypes.Count; index++)
            {
                if (observation.ValidActionTypes[index] == actionType)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
