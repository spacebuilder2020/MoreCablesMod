using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Emit;
using Assets.Scripts;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Util;
using BepInEx.Configuration;
using HarmonyLib;
using LaunchPadBooster.Patching;
using Reagents;
using StationeersMods.Interface;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Label = System.Reflection.Emit.Label;
using Object = UnityEngine.Object;

namespace morecables
{
    [StationeersMod("MoreCables","MoreCables [StationeersMods]","0.7")]
    class MoreCables : ModBehaviour
    {
        private static ConfigEntry<int> _normalVoltage;

        private static ConfigEntry<int> _heavyVoltage;

        private static ConfigEntry<bool> _shEnabled;
        private static ConfigEntry<bool> _scEnabled;
        
        private static ConfigEntry<int> _superHeavyVoltage;

        private static ConfigEntry<int> _superConductorVoltage;
        
        public override void OnLoaded(ContentHandler contentHandler)
        {
            _normalVoltage = Config.Bind("Cables", "normalVoltage", -1, "Voltage for Normal Cables, -1 or below to leave at default");
            _heavyVoltage = Config.Bind("Cables", "heavyVoltage", -1, "Voltage for Heavy Cables, -1 or below to leave at default");
            _shEnabled = Config.Bind("Cables", "superHeavyEnabled", true, "Enable Super Heavy Cables");
            _superHeavyVoltage = Config.Bind("Cables", "superHeavyVoltage", 500000, "Voltage for Super Heavy");
            _scEnabled = Config.Bind("Cables", "superConductorEnabled", true, "Enable Super Conductor Cables");
            _superConductorVoltage = Config.Bind("Cables", "superConductorVoltage", 1000000, "Voltage for Super Conductor");
            ConsoleWindow.Print("Patching Cables");
            Harmony harmony = new Harmony("MoreCables");
            Debug.Log(new StackTrace().GetFrame(0).GetMethod().ReflectedType.Assembly.FullName);
            harmony.ConditionalPatchAll();
            ConsoleWindow.Print("MoreCables Loaded!");
        }

        [HarmonyPatch]
        public class Patches
        {
            [HarmonyPatch(typeof(Cable), nameof(Cable.OnImGuiDraw)), HarmonyTranspiler]
            [HarmonyGameVersionPatch("0.2.0.0", "0.2.6003.26330")]
            private static IEnumerable<CodeInstruction> Cable_OnImGuiDraw_Transpiler(IEnumerable<CodeInstruction> instructions)
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
            [HarmonyPatch(typeof(Cable), nameof(Cable.OnImGuiDraw)), HarmonyTranspiler]
            [HarmonyGameVersionPatch("0.2.6003.26330", "0.2.9999.99999")]
            private static IEnumerable<CodeInstruction> Cable_OnImGuiDraw_Transpiler2(IEnumerable<CodeInstruction> instructions)
            {
                List<CodeInstruction> codes = new List<CodeInstruction>();
                foreach (var instruction in instructions)
                {
                    if (instruction.opcode == OpCodes.Ble_Un && codes[codes.Count - 1].opcode == OpCodes.Ldc_I4_1)
                    {
                        codes[codes.Count - 1].opcode = OpCodes.Ldc_I4_2;
                    }
                    
                    codes.Add(instruction);
                }
                
                return codes.AsEnumerable();
            }
            
            private static List<GameObject> copies = new List<GameObject>();
            private static T CopyPrefab<T>(T srcPrefab, string prefabName, MultiConstructor toolItem) where T : Thing
            {
                var prefab = WorldManager.Instance.SourcePrefabs.Select(p => p as T).FirstOrDefault(p => p?.PrefabName == prefabName);
                if (prefab)
                {
                    return prefab;
                }
                prefab = Object.Instantiate(srcPrefab);
                copies.Add(prefab.gameObject);

                prefab.PrefabName = prefabName;
                prefab.name = prefab.PrefabName; //The name needs to be reset and due to how saving and tooltips work, this must match the prefab name.

                prefab.Renderers.Clear();  //Due to how dynamic loading works, objects will become awake so you need to clear the attached renderers.  Warning, do not delete them or that will break the game!

                prefab.PrefabHash = Animator.StringToHash(prefab.PrefabName); //Once the PrefabName has been defined, a new hash must be generated.

                if (prefab is DynamicThing)  //If your base prefab is a dynamic thing, you need to turn physics back off since by default when an object is created, it is woken up but for our usecase, we need a sleeping item.
                {
                    Traverse.Create(prefab).Field("_slotStateDirty").SetValue(false);
                    Traverse.Create(prefab).Field("_staticParent").SetValue(true);
                }

                if (prefab is MultiConstructor item)
                {
                    item.Constructables.Clear(); //Clear existing Constructables, we will register new Constructables when each of the structures are loaded.
                }

                if (prefab is Structure structure && toolItem)
                {
                    structure.BuildStates[0].Tool.ToolEntry = toolItem; //If a prefab is a structure, a copy must be taken of the tool item for creating a new item varient of the structure.
                    toolItem.Constructables.Add(structure); //For a lot of structures, there is one item that is shared between all    
                }

                WorldManager.Instance.SourcePrefabs.Add(prefab); //Once our new prefab is loaded we need to add it to the list.
                copies.Add(prefab.gameObject); //We also need to save the gameobject so we can inactivate it later.

                return prefab;
            }
                
            [HarmonyPatch(typeof(Prefab), "LoadAll"), HarmonyPrefix]
            private static bool Prefab_Load_All_Prefix()
            {
                ConsoleWindow.Print("Updating Cable Prefabs");
                
                var items = new MultiMergeConstructor[2];
                var cables = WorldManager.Instance.SourcePrefabs.Select(thing => thing as Cable).Where(thing => thing).ToList();

                if (!_shEnabled.Value)
                {
                    ConsoleWindow.PrintAction("Warning: Super Heavy Cables are disabled and will be removed from worlds if loaded!");
                }
                if (!_scEnabled.Value)
                {
                    ConsoleWindow.PrintAction("Warning: Super Conductor Cables are disabled and will be removed from worlds if loaded!");
                }

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
                        default:
                            continue;
                    }
                    Debug.Log($"Cable( Name: {srcCable.name}, Prefab: {srcCable.PrefabName}, Voltage: {srcCable.MaxVoltage}, Type: {(int) srcCable.CableType}) updated");
                    
                    if (!_shEnabled.Value && srcCable.CableType is Cable.Type.normal) continue;
                    if (!_scEnabled.Value && srcCable.CableType is Cable.Type.heavy) continue;
                    
                    int cableType = (int)srcCable.CableType;
                    string type = "";
                    if (srcCable.PrefabName.Contains("Straight"))
                    {
                        type = "Straight";
                    } else if (srcCable.PrefabName.Contains("Corner"))
                    {
                        type = "Corner";
                    } else if (srcCable.PrefabName.Contains("Junction"))
                    {
                        type = "Junction";
                    }

                    char num = srcCable.PrefabName[srcCable.PrefabName.Length -1];
                    
                    if (items[cableType] is null)
                    {
                        items[cableType] = (MultiMergeConstructor) CopyPrefab(srcCable.BuildStates[0].Tool.ToolEntry, cableType == 1
                            ? "ItemCableCoilSuperConductor"
                            : "ItemCableCoilSuperHeavy", null);
                    }
                    
                    var cable = CopyPrefab(srcCable,$"StructureCable{type}{(cableType == 1 ? "SC" : "SH")}{(num >= '0' && num <= '9' ? num.ToString() : "")}", items[cableType]);
                    
                    switch (cable.CableType)
                    {
                        case Cable.Type.normal:
                            if (_superHeavyVoltage.Value >= 0) cable.MaxVoltage = _superHeavyVoltage.Value;
                            break;
                        case Cable.Type.heavy:
                            if (_superConductorVoltage.Value >= 0) cable.MaxVoltage = _superConductorVoltage.Value;
                            break;
                    }
                    
                    cable.CableType = (Cable.Type) (cableType + 2);
                    
                    cable.RupturedPrefab = CopyPrefab(cable.RupturedPrefab, cable.PrefabName + "Burnt", null);
                    
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
            [HarmonyPatch(typeof(WorldManager), "LoadDataFiles"), HarmonyPrefix]
            [HarmonyGameVersionPatch("0.2.0.0", "0.2.6003.26330")]
            private static bool WorldManager_LoadDataFiles_Prefix()
            {
                ElectronicsPrinter.RecipeComparable.AddRecipe(new WorldManager.RecipeData
                {
                    PrefabName = "ItemCableCoilSuperHeavy",
                    Recipe = new Recipe {Time = 5f, Energy = 1000, Electrum = 0.5},
                }, new ModAbout {Name = "MoreCables"});
                return true;
            }
        }
    }
}