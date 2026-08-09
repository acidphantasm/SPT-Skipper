using System;
using System.Linq;
using System.Reflection;
using SPT.Reflection.Patching;
using EFT;
using EFT.Quests;
using EFT.UI;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace Terkoiz.Skipper
{
    using Object=UnityEngine.Object;

    public class QuestObjectiveViewPatch : ModulePatch
    {
        private static Type _underlyingQuestControllerType;
        internal static GameObject LastSeenObjectivesBlock;
        
        
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(QuestObjectiveView), nameof(QuestObjectiveView.Show));
        }

        [PatchPostfix]
        private static void PatchPostfix([CanBeNull]DefaultUIButton ____handoverButton, QuestController questController, Condition condition, Quest quest, QuestObjectiveView __instance)
        {
            if (!SkipperPlugin.ModEnabled.Value)
                return;

            // The handover button is usually only missing in the non-trader task view screens, where we don't want to allow skipping either way
            if (____handoverButton == null)
                return;

            if (_underlyingQuestControllerType == null)
                ResolveQuestControllerClass();

            LastSeenObjectivesBlock = __instance.transform.parent.gameObject;

            var skipButton = Object.Instantiate(____handoverButton, ____handoverButton.transform.parent.transform);

            skipButton.SetRawText("SKIP", 22);
            skipButton.gameObject.name = SkipperPlugin.SkipButtonName;
            skipButton.gameObject.GetComponent<UnityEngine.UI.LayoutElement>().minWidth = 100f;
            skipButton.gameObject.SetActive(SkipperPlugin.AlwaysDisplay.Value && !quest.IsConditionDone(condition));
            
            skipButton.OnClick.RemoveAllListeners();
            skipButton.OnClick.AddListener(() => ItemUiContext.Instance.ShowMessageWindow(
                description: "Are you sure you want to autocomplete this quest objective?",
                acceptAction: () =>
                {
                    if (quest.IsConditionDone(condition))
                    {
                        skipButton.gameObject.SetActive(false);
                        return;
                    }

                    SkipperPlugin.Logger.LogDebug($"Setting condition {condition.id} value to {condition.value}");

                    // This line will force any condition checker to pass, as the 'condition.value' field contains the "goal" of any quest condition
                    quest.ProgressCheckers[condition].SetCurrentValueGetter(_ => condition.value);

                    SetConditionCurrentValue(questController, quest, condition);

                    skipButton.gameObject.SetActive(false);
                },
                cancelAction: () => {},
                caption: "Confirmation"));
        }

        private static void ResolveQuestControllerClass()
        {
            _underlyingQuestControllerType = AccessTools.GetTypesFromAssembly(typeof(AbstractGame).Assembly)
                .SingleOrDefault(t =>
                    t.GetEvent(
                    "OnConditionQuestTimeExpired",
                    BindingFlags.DeclaredOnly |
                    BindingFlags.Public |
                    BindingFlags.Instance
                    ) != null
                );

            if (_underlyingQuestControllerType == null)
            {
                SkipperPlugin.Logger.LogError("Failed to locate ConditionsConnectorsManagerClient");
                return;
            }

            SkipperPlugin.Logger.LogDebug($"Resolved underlying quest controller type to {_underlyingQuestControllerType.FullName}");
        }
        
        private static void SetConditionCurrentValue(QuestController questController, Quest quest, Condition condition)
        {
            var conditionControllerField = questController.GetType()
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .SingleOrDefault(field =>
                    field.FieldType.IsGenericType &&
                    field.FieldType.GetGenericTypeDefinition() == _underlyingQuestControllerType
                );

            if (conditionControllerField == null)
            {
                SkipperPlugin.Logger.LogError($"Failed to locate {_underlyingQuestControllerType.Name} field on {questController.GetType().Name}");
                return;
            }

            var conditionController = conditionControllerField.GetValue(questController);
            AccessTools.Method(conditionController.GetType(), "SetConditionCurrentValue")?.Invoke(
            conditionController,
            [
                quest,
                EQuestStatus.AvailableForFinish,
                condition,
                condition.value,
                true
            ]);
        }
    }
}