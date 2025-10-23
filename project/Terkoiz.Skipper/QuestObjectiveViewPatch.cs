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
    public class QuestObjectiveViewPatch : ModulePatch
    {
        private static string UnderlyingQuestControllerClassName;
        internal static GameObject LastSeenObjectivesBlock;
        
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(QuestObjectiveView), nameof(QuestObjectiveView.Show));
        }

        [PatchPostfix]
        private static void PatchPostfix([CanBeNull]DefaultUIButton ____handoverButton, AbstractQuestControllerClass questController, Condition condition, QuestClass quest, QuestObjectiveView __instance)
        {
            if (!SkipperPlugin.ModEnabled.Value)
            {
                return;
            }

            // The handover button is usually only missing in the non-trader task view screens, where we don't want to allow skipping either way
            if (____handoverButton == null)
            {
                return;
            }

            if (UnderlyingQuestControllerClassName == null)
            {
                var type = AccessTools.GetTypesFromAssembly(typeof(AbstractGame).Assembly)
                    .SingleOrDefault(t => t.GetEvent("OnConditionQuestTimeExpired", BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance) != null);
                
                if (type == null)
                {
                    SkipperPlugin.Logger.LogError("Failed to locate a specific quest controller type");
                    return;
                }

                UnderlyingQuestControllerClassName = type.Name.Split('`')[0];
                SkipperPlugin.Logger.LogDebug($"Resolved {nameof(UnderlyingQuestControllerClassName)} to be {UnderlyingQuestControllerClassName}");
            }

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

                    // This only applies to SPT 4.0 because the `UnderlyingQuestControllerClassName` is no longer all lowercase, nor is it the same as the class name. It's now UpperAllLower instead of UpperUpperAllLower or AllLower.
                    var spt400UnderlyingQuestControllerClassName = LowerCaseAllButFirst(UnderlyingQuestControllerClassName);
                    // We call 'SetConditionCurrentValue' to trigger all the code needed to make the condition completion appear visually in-game
                    var conditionController = AccessTools.Field(questController.GetType(), $"{spt400UnderlyingQuestControllerClassName}_0").GetValue(questController);
                    AccessTools.DeclaredMethod(conditionController.GetType().BaseType, "SetConditionCurrentValue").Invoke(conditionController, new object[] { quest, EQuestStatus.AvailableForFinish, condition, condition.value, true });

                    skipButton.gameObject.SetActive(false);
                },
                cancelAction: () => {},
                caption: "Confirmation"));
        }

        public static string LowerCaseAllButFirst(string input)
        {
            var firstCharacter = input[0];
            var restOfString = input.Substring(1).ToLower();
            return firstCharacter + restOfString;
        }
    }
}