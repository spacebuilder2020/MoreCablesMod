using System;
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
using Console = Assets.Scripts.Objects.Electrical.Console;
using Label = System.Reflection.Emit.Label;
using Object = UnityEngine.Object;

namespace morecables
{
    [StationeersMod("MoreCables","MoreCables [StationeersMods]","0.5")]
    class MoreCables : ModBehaviour
    {
        public static ConfigEntry<int> normalVoltage;
        
        public static ConfigEntry<int> heavyVoltage;
        
        public static ConfigEntry<int> superHeavyVoltage;
        
        public static ConfigEntry<int> superConductorVoltage;
        
        public override void OnLoaded(ContentHandler contentHandler)
        {
            normalVoltage = Config.Bind("Cables", "normalVoltage", -1, "-1 or below to leave at default");
            heavyVoltage = Config.Bind("Cables", "heavyVoltage", -1, "-1 or below to leave at default");
            
            superHeavyVoltage = Config.Bind("Cables", "superHeavyVoltage", 500000);
            superConductorVoltage = Config.Bind("Cables", "superConductorVoltage", 1000000);
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
        
        [HarmonyPatch(typeof(Prefab), "LoadAll"), HarmonyPrefix]
        private static bool Prefab_Load_All_Prefix()
        {
            ConsoleWindow.Print("Updating Cable Prefabs");
            
            var items = WorldManager.Instance.SourcePrefabs.Select(thing => thing as MultiMergeConstructor)
                .Where(thing => thing && thing.PrefabName.StartsWith("ItemCableCoil")).ToList();

            if (items.Count != 2)
            {
                throw new ArgumentException("Unexpected number of items found, can not continue!");
            }

            for (var i = 0; i < items.Count; i++)
            {
                var srcItem = items[i];
                MultiMergeConstructor item = UnityEngine.Object.Instantiate(srcItem);

                item.PrefabName = item.PrefabName.Contains("Heavy")
                    ? "ItemCableCoilSuperConductor"
                    : "ItemCableCoilSuperHeavy";

                item.name = item.PrefabName;
                item.PrefabHash = Animator.StringToHash(item.PrefabName);
                item.Constructables.Clear();

                Traverse.Create(item).Field("_slotStateDirty").SetValue(false);
                Traverse.Create(item).Field("_staticParent").SetValue(true);
                
                WorldManager.Instance.SourcePrefabs.Add(item);
                items[i] = item;
            }

            var cables = WorldManager.Instance.SourcePrefabs.Select(thing => thing as Cable).Where(thing => thing).ToList();
            
            foreach (var srcCable in cables)
            {
                switch (srcCable.CableType)
                {
                    case Cable.Type.normal:
                        if (normalVoltage.Value >= 0) srcCable.MaxVoltage = normalVoltage.Value;
                        break;
                    case Cable.Type.heavy:
                        if (heavyVoltage.Value >= 0) srcCable.MaxVoltage = heavyVoltage.Value;
                        break;
                }
                Debug.Log($"Cable( Name: {srcCable.name}, Prefab: {srcCable.PrefabName}, Voltage: {srcCable.MaxVoltage}, Type: {(int) srcCable.CableType}) updated");
                
                var cable = Object.Instantiate(srcCable);
                
                var getCableName = new Func<Cable, string>((Cable c) =>
                {
                    string type = "";
                    if (c.PrefabName.Contains("Straight"))
                    {
                        type = "Straight";
                    } else if (c.PrefabName.Contains("Corner"))
                    {
                        type = "Corner";
                    } else if (c.PrefabName.Contains("Junction"))
                    {
                        type = "Junction";
                    }

                    char n = c.PrefabName[c.PrefabName.Length -1];
                    bool hasNum = n >= '0' && n <= '9';
                    bool isHeavy = c.CableType == Cable.Type.heavy;
                    
                    return $"StructureCable{type}{(isHeavy ? "SC" : "SH")}{(hasNum ? n.ToString() : "")}";
                });
                cable.PrefabName = getCableName(cable);
                cable.name = cable.PrefabName;
                cable.PrefabHash = Animator.StringToHash(cable.PrefabName);
                
                switch (cable.CableType)
                {
                    case Cable.Type.normal:
                        if (superHeavyVoltage.Value >= 0) cable.MaxVoltage = superHeavyVoltage.Value;
                        break;
                    case Cable.Type.heavy:
                        if (superConductorVoltage.Value >= 0) cable.MaxVoltage = superConductorVoltage.Value;
                        break;
                    
                }
                
                cable.BuildStates[0].Tool.ToolEntry = items[(int)cable.CableType];
                items[(int)cable.CableType].Constructables.Add(cable);
                
                cable.CableType = (Cable.Type) ((int)cable.CableType + 2);
                
                
                var burntCable = Object.Instantiate(cable.RupturedPrefab);
                
                burntCable.PrefabName = cable.PrefabName += "Burnt";
                burntCable.name = burntCable.PrefabName;
                burntCable.PrefabHash = Animator.StringToHash(burntCable.PrefabName);
                cable.RupturedPrefab = burntCable;
                
                WorldManager.Instance.SourcePrefabs.Add(burntCable);
                WorldManager.Instance.SourcePrefabs.Add(cable);
                Debug.Log($"Cable( Name: {cable.name}, Prefab: {cable.PrefabName}, Voltage: {cable.MaxVoltage}, Type: {(int) cable.CableType}) added");
            }
            
            ConsoleWindow.Print("Done");
            return true;
        }
        }
    }
}