//using forgotten_construction_set;
using HarmonyLib;
using Steamworks;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using static FCS_extended.FCS_extended;

namespace FCS_extended
{
	internal class FCS_extended
	{
		// loaded after vanilla
		private static List<string> defFiles = new List<string>();
		// loaded before vanilla
		private static List<string> preloadDefFiles = new List<string>();
		private static Assembly assembly = null;
		private static Dictionary<string, List<KeyValuePair<Regex, string>>> defFilePatches = new Dictionary<string, List<KeyValuePair<Regex, string>>>();

		[STAThread]
		static void Main(string[] args)
		{
			Console.WriteLine("Finding FCS...");
			string path = Path.Combine(Directory.GetCurrentDirectory(), "forgotten construction set.exe");
			if (!File.Exists(path))
				path = Path.Combine(Directory.GetCurrentDirectory(), "FCS_gog.exe");
			if (!File.Exists(path))
			{
				Console.WriteLine("Could not find FCS executable, please copy FCS_extended to your Kenshi install dir.");
				return;
			}

			Console.WriteLine("Found FCS, loading " + path + "...");
			assembly = Assembly.LoadFile(path);

            Console.WriteLine("Scanning for plugins...");

            // try get Steam Workshop mod dir
            List<string> steamMods = new List<string>();
            if (path.EndsWith("forgotten construction set.exe"))
			{
				SteamAPI.Init();
				uint numItems = SteamUGC.GetNumSubscribedItems();
				PublishedFileId_t[] items = new PublishedFileId_t[numItems];
				SteamUGC.GetSubscribedItems(items, numItems);

				foreach (PublishedFileId_t item in items)
				{
					if ((SteamUGC.GetItemState(item) & (uint)EItemState.k_EItemStateInstalled) > 0)
					{
						ulong size;
						string folder;
						uint timestamp;
						// MAX_PATH = 260
						if (SteamUGC.GetItemInstallInfo(item, out size, out folder, 260, out timestamp))
							if(File.Exists(folder))
								steamMods.Add(folder);
					}
				}
			}


            foreach (string dir in Directory.EnumerateDirectories("mods").Concat(steamMods))
			{
				string jsonPath = Path.Combine(dir, "FCS_extended.json");
				if (File.Exists(jsonPath))
				{
					Console.WriteLine(jsonPath);
					try
					{
						JsonDocument doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
						foreach (JsonElement elem in doc.RootElement.GetProperty("FCS_Plugins").EnumerateArray())
						{
							// TODO context?
							string pluginPath = Path.Combine(dir, elem.GetString());
							Console.WriteLine(pluginPath);
							Assembly pluginAss = Assembly.LoadFrom(pluginPath);
							foreach (Type pluginType in pluginAss.GetTypes().Where(t => typeof(IPlugin).IsAssignableFrom(t)))
							{
								if (Activator.CreateInstance(pluginType) is IPlugin result)
								{
									result.Init(assembly);
								}
							}
						}
					}
					catch (Exception e)
					{
						Console.WriteLine(e);
					}
				}

				if (File.Exists(Path.Combine(dir, "fcs.def")))
				{
					defFiles.Add(Path.Combine(dir, "fcs.def"));
				}
				if (File.Exists(Path.Combine(dir, "fcs_preload.def")))
				{
					preloadDefFiles.Add(Path.Combine(dir, "fcs_preload.def"));
				}

				foreach (string file in Directory.GetFiles(dir, "*.def.patch"))
				{
					string fileName = Path.GetFileName(file);
					string target = Path.GetFileName(file).Substring(0, fileName.LastIndexOf(".patch"));
					if (!defFilePatches.ContainsKey(target))
						defFilePatches.Add(target, new List<KeyValuePair<Regex, string>>());
					StreamReader stream = new StreamReader(File.OpenRead(file));
					string line;
					while ((line = stream.ReadLine()) != null && line != "")
					{
						string replacement = stream.ReadLine();
						if (replacement == null)
							replacement = "";
						defFilePatches[target].Add(new KeyValuePair<Regex, string>(new Regex(line), replacement));
					}
					stream.Close();
					Console.WriteLine(Path.GetFileName(dir) + " loaded patch for: " + target);
				}
			}

			Harmony harmony = new Harmony("FCS_extended");
			Harmony.DEBUG = true;

			harmony.PatchAll();

            Console.WriteLine("Starting FCS...");
			object[] argsWrapped = new object[1];
			argsWrapped[0] = args;
			assembly.EntryPoint.Invoke(null, argsWrapped);

		}

		// patch to allow merging of FCS layout sections
		[HarmonyPatch("forgotten_construction_set.navigation", "clearCategories")]
		public static class navigation_clearCategories_Patch
		{
			[HarmonyPrefix]
			static bool Prefix()
			{
				// drop ClearCategories calls so layout sections are merged together
				return false;
			}
		}

		// Fix weirdness in Definitions parse functions
		[HarmonyPatch("Type", "GetType")]
		[HarmonyPatch(new Type[] { typeof(string), typeof(bool) })]
		public class Type_GetType_Patch
		{
			[HarmonyPostfix]
			static void Postfix(ref Type __result, string typeName, bool throwOnError)
			{
				if (__result == null)
				{
					__result = assembly.GetType(typeName);
				}
			}
		}

		// patch to load mod def files
		[HarmonyPatch("forgotten_construction_set.Definitions", "Load")]
		public class Definitions_Load_Patch
		{
			private static bool initialized = false;
			private static bool preloaded = false;
			[HarmonyPrefix]
			static void Prefix(string filename, dynamic nav)
			{
				if(!preloaded)
				{
					preloaded = true;
					// load custom definitions
					Type baseDefinitions_type = assembly.GetType("forgotten_construction_set.Definitions");
					MethodInfo baseDefinitions_Load = AccessTools.Method("forgotten_construction_set.Definitions:Load");
					foreach (string defFile in preloadDefFiles)
					{
						baseDefinitions_Load.Invoke(baseDefinitions_type, new object[] { defFile, nav });
					}
				}
				if (filename.EndsWith("/settings.def") && !initialized)
				{
					initialized = true;
					// load custom definitions
					Type baseDefinitions_type = assembly.GetType("forgotten_construction_set.Definitions");
					MethodInfo baseDefinitions_Load = AccessTools.Method("forgotten_construction_set.Definitions:Load");
					foreach (string defFile in defFiles)
					{
						//Console.WriteLine(defFile);
						baseDefinitions_Load.Invoke(baseDefinitions_type, new object[] { defFile, nav });
					}
				}
				Console.WriteLine(filename);
			}
			[HarmonyTranspiler]
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				MethodInfo FileStream_CopyTo = AccessTools.Method("Stream:CopyTo", new Type[] { typeof(Stream) });

				foreach (var instruction in instructions)
				{
					if (instruction.Calls(FileStream_CopyTo))
					{
						Console.WriteLine("Patched CopyTo");
						instruction.opcode = OpCodes.Call;
						instruction.operand = typeof(FCS_extended).GetMethod("Stream_CopyTo");
					}
				}

				return instructions;
			}
		}

		// used to add regex patches to def files
		public static void Stream_CopyTo(Stream __instance, Stream destination)
		{
			FileStream fileStream = __instance as FileStream;
			if (fileStream != null)
			{
				string fileName = Path.GetFileName(fileStream.Name);
				// this condition can be used to only apply patches to 
				// Directory.GetParent(fileStream.Name).Parent.Name != "mods";
				if (defFilePatches.ContainsKey(fileName))
				{
					Console.WriteLine("Patching " + fileName);

					string fileContents;
					StreamReader reader = new StreamReader(fileStream);
					fileContents = reader.ReadToEnd();

					foreach (KeyValuePair<Regex, string> patch in defFilePatches[fileName])
						fileContents = patch.Key.Replace(fileContents, patch.Value);
					//Console.WriteLine(fileContents);
					// write out to stream
					//new StreamWriter(destination).Write(fileContents);


					byte[] array = Encoding.ASCII.GetBytes(fileContents);
					destination.Write(array, 0, array.Length);

					return;
				}
			}
			__instance.CopyTo(destination);

		}

		// generics in Harmony are completely fucked so we patch the caller IL to redirect calls instead of hooking the called function
		// patch to allow merging of enum definitions
		[HarmonyPatch("forgotten_construction_set.Definitions", "ParseEnum")]
		public static class Definitions_ParseEnum_Patch
		{
			[HarmonyTranspiler]
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				Type Dictionary_Type = typeof(Dictionary<,>).MakeGenericType(new Type[] { typeof(string), AccessTools.TypeByName("forgotten_construction_set.FCSEnum") });

				foreach (var instruction in instructions)
				{
					if (instruction.Calls(Dictionary_Type.Method("ContainsKey")))
					{
						Console.WriteLine("Patched ContainsKey");
						instruction.opcode = OpCodes.Call;
						instruction.operand = typeof(FCS_extended).GetMethod("Dictionary_ContainsKey")
							.MakeGenericMethod(new Type[] { AccessTools.TypeByName("forgotten_construction_set.FCSEnum") });
					}
					if (instruction.Calls(Dictionary_Type.Method("Add")))
					{
						Console.WriteLine("Patched Add");
						instruction.opcode = OpCodes.Call;
						instruction.operand = typeof(FCS_extended).GetMethod("Dictionary_Add")
							.MakeGenericMethod(new Type[] { AccessTools.TypeByName("forgotten_construction_set.FCSEnum") });
					}
				}
				return instructions;
			}
		}

		// generic so we can set the correct arg type without statically linking to the FCS, which breaks simultaneous Steam + GOG compatibility
		public static void Dictionary_Add<T>(Dictionary<string, T> __instance, string key, T value)
		{
			IEnumerable<KeyValuePair<string, int>> valueEnumerable = value as IEnumerable<KeyValuePair<string, int>>;
			if (!__instance.ContainsKey(key) && valueEnumerable.Count() == 0)
			{
				// this signals initialization from built-in enums
				Console.WriteLine("Generating default values for " + key);

				foreach (object enumVal in Enum.GetValues(AccessTools.TypeByName("forgotten_construction_set." + key)))
				{
					//Console.WriteLine(enumVal.ToString()  + " " + (int)enumVal);
					typeof(T).GetMethod("AddValue", new Type[] { typeof(string), typeof(int), typeof(string) }).Invoke(value,
						new object[] { enumVal.ToString(), (int)enumVal, null });
				}
			}
			if (__instance.ContainsKey(key))
			{
				Console.WriteLine("Merging " + valueEnumerable.Count() + " keys for " + key);
				foreach (KeyValuePair<string, int> entry in valueEnumerable)
				{
					//Console.WriteLine(entry);
					// TODO comments
					typeof(T).GetMethod("AddValue", new Type[] { typeof(string), typeof(int), typeof(string) }).Invoke(__instance[key],
						new object[] { entry.Key, entry.Value, null });

				}
			}
			else
			{
				__instance.Add(key, value);
			}
		}

		// hack to make FCS pass new enums to the above Dictionary.Add()
		public static bool Dictionary_ContainsKey<T>(Dictionary<string, T> __instance, string key)
		{
			return false;
		}

		// patch to override enums with FCS definitions
		[HarmonyPatch("Enum", "GetValues")]
		public static class Enum_GetValues_Patch
		{
			[HarmonyPrefix]
			static bool Prefix(ref Array __result, Type enumType)
			{
				IDictionary fcsenums_types = (IDictionary)Traverse.Create(AccessTools.TypeByName("forgotten_construction_set.FCSEnums")).Field("types").GetValue();
				if (fcsenums_types.Contains(enumType.Name))
				{
					IEnumerable<KeyValuePair<string, int>> fcsEnum = fcsenums_types[enumType.Name] as IEnumerable<KeyValuePair<string, int>>;

					__result = Array.CreateInstance(enumType, fcsEnum.Count());
					int i = 0;
					foreach (KeyValuePair<string, int> entry in fcsEnum)
					{
						__result.SetValue(Enum.ToObject(enumType, entry.Value), i);
						++i;
					}
					return false;
				}
				return true;
			}
		}

		// patch to make enum name show up correctly in UI
		[HarmonyPatch(typeof(Enum), "ToString", new Type[] { })]
		public static class Enum_ToString_Patch
		{
			[HarmonyPostfix]
			static void Postfix(Enum __instance, ref string __result)
			{
				IDictionary fcsenums_types = (IDictionary)Traverse.Create(AccessTools.TypeByName("forgotten_construction_set.FCSEnums")).Field("types").GetValue();
				if (fcsenums_types.Contains(__instance.GetType().Name))
				{
					IEnumerable<KeyValuePair<string, int>> fcsEnum = fcsenums_types[__instance.GetType().Name] as IEnumerable<KeyValuePair<string, int>>;
					// TODO figure out a less hacky way to do this that doesn't suck
					foreach (KeyValuePair<string, int> entry in fcsEnum)
					{
						if (entry.Value == (int)(object)__instance)
						{
							__result = entry.Key;
						}
					}
				}
			}
		}


		// patch to sort out reading enums back from the UI
		[HarmonyPatch("Enum", "Parse")]
		[HarmonyPatch(new Type[] { typeof(Type), typeof(string) })]
		public static class Enum_Parse_Patch
		{
			[HarmonyPrefix]
			static bool Prefix(ref object __result, Type enumType, string value)
			{
				IDictionary fcsenums_types = (IDictionary)Traverse.Create(AccessTools.TypeByName("forgotten_construction_set.FCSEnums")).Field("types").GetValue();
				if (fcsenums_types.Contains(enumType.Name))
				{
					//Console.WriteLine("Finding...");
					IEnumerable<KeyValuePair<string, int>> fcsEnum = fcsenums_types[enumType.Name] as IEnumerable<KeyValuePair<string, int>>;

					foreach (KeyValuePair<string, int> entry in fcsEnum)
					{
						if (entry.Key == value)
						{
							__result = Enum.ToObject(enumType, entry.Value);
							return false;
						}
					}
				}

				return true;
			}
		}

		// patch to fix the dialogue condition tag box for new conditions
		[HarmonyPatch("forgotten_construction_set.dialog.ConditionControl", "refreshGrid")]
		public static class ConditionControl_refreshGrid_Patch
		{
			// workaround to cause all custom dialogue condition tag enums to write out as an integer
			// (or at least ones for conditions >= to DC_PERSONALITY_TAG)
			enum NUMBER
			{

			}

			// fixes parsing of dialogue condition tags - all conditions >= DC_PERSONALITY_TAG have their tag
			// added to the UI as an integer, to be parsed + replaced later
			// we patch the following check to replace the PersonalityTags cast to NUMBER which has no entries
			// so NUMBER.ToString() just converts the enum  value to it's corresponding integer
			// if (dialogConditionEnum2 >= DialogConditionEnum.DC_PERSONALITY_TAG)
			// {
			//		listViewItem.SubItems.Add(((PersonalityTags) item.idata["tag"]).ToString());
			// }
			[HarmonyTranspiler]
			static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			{
				Type personaltyTags = AccessTools.TypeByName("forgotten_construction_set.PersonalityTags");

				foreach (var instruction in instructions)
				{
					if (instruction.opcode == OpCodes.Constrained)
					{
						if ((Type)instruction.operand == personaltyTags)
						{
							instruction.operand = typeof(NUMBER);
						}
					}
				}
				return instructions;
			}

			// patch to convert the integer enum values from the previous function back to their string equivalents
			// so they show up in the UI correctly
			[HarmonyPostfix]
			static void Postfix(object __instance, object dialogLine)
			{
				if (dialogLine != null)
				{
					ListView listView1conditions = Traverse.Create(__instance).Field("listView1conditions").GetValue() as ListView;
					foreach (ListViewItem item in listView1conditions.Items)
					{
						if (item.SubItems.Count >= 5)
						{
							// fix enum
							IDictionary conditionDefaults = (IDictionary)Traverse.Create(AccessTools.TypeByName("forgotten_construction_set.dialog.ConditionControl")).Field("conditionDefaults").GetValue();
							object conditionEnum = Enum.Parse(AccessTools.TypeByName("forgotten_construction_set.DialogConditionEnum"), item.SubItems[1].Text);
							if (conditionDefaults.Contains(conditionEnum))
							{
								int result;
								// if the value is an int instead of an enum name, convert it to it's enum
								if (int.TryParse(item.SubItems[4].Text, out result))
								{
									// remove the old int field
									item.SubItems.RemoveAt(4);

									// use type of default value for enum parsing
									object defaultVal = conditionDefaults[conditionEnum];
									// add the new enum field
									item.SubItems.Add(Enum.ToObject(defaultVal.GetType(), result).ToString());
								}
							}
							else
							{
								// condition isn't supposed to have a tag, so remove the field
								item.SubItems.RemoveAt(4);
							}
						}
					}
				}
			}
		}
		/*
		// patch window title
		[HarmonyPatch("forgotten_construction_set.baseForm", "updateTitle")]
		public static class baseForm_updateTitle
		{
			[HarmonyPostfix]
			static void Postfix(ref object __instance)
			{
				string title = (string)Traverse.Create(__instance).Property("Text").GetValue();
				if (!title.Contains(" Extended "))
					Traverse.Create(__instance).Property("Text").SetValue(title.Replace("Forgotten Construction Set ", "Forgotten Construction Set Extended "));
			}
		}
		*/
	}
}
