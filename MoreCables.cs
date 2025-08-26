using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using Assets.Scripts;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Util;
using BepInEx.Configuration;
using HarmonyLib;
using StationeersMods.Interface;
using UnityEngine;
using Label = System.Reflection.Emit.Label;
using Object = UnityEngine.Object;

namespace morecables
{
    [StationeersMod("MoreCables","MoreCables [StationeersMods]","0.7")]
    class MoreCables : ModBehaviour
    {
        private static ConfigEntry<int> _normalVoltage;

        private static ConfigEntry<int> _heavyVoltage;

        private static ConfigEntry<int> _superHeavyVoltage;

        private static ConfigEntry<int> _superConductorVoltage;
        
        public override void OnLoaded(ContentHandler contentHandler)
        {
            _normalVoltage = Config.Bind("Cables", "normalVoltage", -1, "-1 or below to leave at default");
            _heavyVoltage = Config.Bind("Cables", "heavyVoltage", -1, "-1 or below to leave at default");
            
            _superHeavyVoltage = Config.Bind("Cables", "superHeavyVoltage", 500000);
            _superConductorVoltage = Config.Bind("Cables", "superConductorVoltage", 1000000);
            ConsoleWindow.Print("Patching Cables");
            Harmony harmony = new Harmony("MoreCables");
            harmony.PatchAll();
            ConsoleWindow.Print("MoreCables Loaded!");
        }

        [HarmonyPatch]
        public class Patches
        {
            [HarmonyPatch(typeof(Cable), nameof(Cable.OnImGuiDraw)), HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                List<CodeInstruction> codes = new List<CodeInstruction>();
                Label brFalse = new Label();
                Label beq = new Label();
                foreach (var instruction in instructions)
                {
                    
                    if (instruction.opcode == OpCodes.Brfalse && codes[codes.Count - 1].opcode == OpCodes.Ldloc_S && (codes[codes.Count - 1].operand as LocalBuilder)?.LocalIndex == 6)
                    {
                        brFalse = (Label) instruction.operand;
                    }
                    if (instruction.opcode == OpCodes.Beq && codes[codes.Count - 1].opcode == OpCodes.Ldc_I4_1)
                    {
                        beq = (Label) instruction.operand;
                    }
                    
                    if (instruction.opcode == OpCodes.Throw && codes[codes.Count - 1].opcode == OpCodes.Newobj)
                    {
                        var newobj = codes.Pop();

                        codes.Add(new CodeInstruction(OpCodes.Ldloc_S, 6));
                        codes.Add(new CodeInstruction(OpCodes.Ldc_I4_2));
                        codes.Add(new CodeInstruction(OpCodes.Beq_S, brFalse));
                        
                        codes.Add(new CodeInstruction(OpCodes.Ldloc_S, 6));
                        codes.Add(new CodeInstruction(OpCodes.Ldc_I4_3));
                        codes.Add(new CodeInstruction(OpCodes.Beq_S, beq));
                        
                        codes.Add(newobj);
                    }
                    
                    codes.Add(instruction);
                }
                
                return codes.AsEnumerable();
            }

            private static void Copy<T>(T original, T copy)
            {
                var type = original.GetType();
                foreach (var field in type.GetFields())
                {
                    field.SetValue(copy, field.GetValue(original));
                }
            }
            
            private static List<GameObject> copies = new List<GameObject>();
                
            [HarmonyPatch(typeof(Prefab), "LoadAll"), HarmonyPrefix]
            private static bool Prefab_Load_All_Prefix()
            {
                ConsoleWindow.Print("Updating Cable Prefabs");
                
                var items = new MultiMergeConstructor[2];
                var cables = WorldManager.Instance.SourcePrefabs.Select(thing => thing as Cable).Where(thing => thing).ToList();
                
                foreach (var srcCable in cables)
                {
                    switch (srcCable.CableType)
                    {
                        case Cable.Type.normal:
                            if (_normalVoltage.Value >= 0) srcCable.MaxVoltage = _normalVoltage.Value;
                            break;
                        case Cable.Type.heavy:
                            if (_heavyVoltage.Value >= 0) srcCable.MaxVoltage = _heavyVoltage.Value;
                            break;
                    }
                    Debug.Log($"Cable( Name: {srcCable.name}, Prefab: {srcCable.PrefabName}, Voltage: {srcCable.MaxVoltage}, Type: {(int) srcCable.CableType}) updated");
                    
                    var cable = Object.Instantiate(srcCable);
                    copies.Add(cable.gameObject);
                    int cableType = (int)cable.CableType;
                    string type = "";
                    if (cable.PrefabName.Contains("Straight"))
                    {
                        type = "Straight";
                    } else if (cable.PrefabName.Contains("Corner"))
                    {
                        type = "Corner";
                    } else if (cable.PrefabName.Contains("Junction"))
                    {
                        type = "Junction";
                    }

                    char num = cable.PrefabName[cable.PrefabName.Length -1];
                        
                    cable.PrefabName = $"StructureCable{type}{(cableType == 1 ? "SC" : "SH")}{(num >= '0' && num <= '9' ? num.ToString() : "")}";
                    
                    cable.name = cable.PrefabName;
                    cable.Renderers.Clear();
                    cable.PrefabHash = Animator.StringToHash(cable.PrefabName);
                    
                    switch (cable.CableType)
                    {
                        case Cable.Type.normal:
                            if (_superHeavyVoltage.Value >= 0) cable.MaxVoltage = _superHeavyVoltage.Value;
                            break;
                        case Cable.Type.heavy:
                            if (_superConductorVoltage.Value >= 0) cable.MaxVoltage = _superConductorVoltage.Value;
                            break;
                    }
                    
                    if (items[cableType] is null)
                    {
                        Debug.Log($"Creating Item");
                        MultiMergeConstructor item = (MultiMergeConstructor) UnityEngine.Object.Instantiate(cable.BuildStates[0].Tool.ToolEntry);
                        copies.Add(item.gameObject);
                        item.PrefabName = cableType == 1
                            ? "ItemCableCoilSuperConductor"
                            : "ItemCableCoilSuperHeavy";

                        item.name = item.PrefabName;
                        item.Renderers.Clear();
                        item.PrefabHash = Animator.StringToHash(item.PrefabName);
                        item.Constructables.Clear();

                        Traverse.Create(item).Field("_slotStateDirty").SetValue(false);
                        Traverse.Create(item).Field("_staticParent").SetValue(true);
                    
                        WorldManager.Instance.SourcePrefabs.Add(item);
                        items[cableType] = item;
                    }
                    
                    cable.BuildStates[0].Tool.ToolEntry = items[cableType];
                    items[cableType].Constructables.Add(cable);
                    
                    cable.CableType = (Cable.Type) (cableType + 2);
                    
                    Debug.Log($"Creating Ruptured");
                    var burntCable = Object.Instantiate(cable.RupturedPrefab);
                    copies.Add(burntCable.gameObject);
                    burntCable.PrefabName = cable.PrefabName + "Burnt";
                    burntCable.name = burntCable.PrefabName;
                    burntCable.Renderers.Clear();
                    burntCable.PrefabHash = Animator.StringToHash(burntCable.PrefabName);
                    cable.RupturedPrefab = burntCable;
                    
                    WorldManager.Instance.SourcePrefabs.Add(burntCable);
                    WorldManager.Instance.SourcePrefabs.Add(cable);
                    Debug.Log($"Cable( Name: {cable.name}, Prefab: {cable.PrefabName}, Voltage: {cable.MaxVoltage}, Type: {(int) cable.CableType}) added");
                }

                Prefab.OnPrefabsLoaded += Prefab_OnPrefabsLoaded;
                ConsoleWindow.Print("Done");
                return true;
            }
            
            private static void Prefab_OnPrefabsLoaded()
            {
                foreach (var copy in copies)
                {
                    copy.SetActive(false);
                }
            }
        }
    }
}