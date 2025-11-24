//using forgotten_construction_set;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FCS_extended
{
	internal class Program
	{
		private static List<string> defFiles = new List<string>();
		private static Assembly assembly = null;

		[STAThread]
		static void Main(string[] args)
		{
			Console.WriteLine("Finding FCS...");
			string path = Path.Combine(Directory.GetCurrentDirectory(), "forgotten construction set.exe");
			if(!File.Exists(path))
				path = Path.Combine(Directory.GetCurrentDirectory(), "FCS_gog.exe");
			if (!File.Exists(path))
			{
				Console.WriteLine("Could not find FCS executable, please copy FCS_extended to your Kenshi install dir.");
				return;
			}

			Console.WriteLine("Found FCS, loading "+ path + "...");
			assembly = Assembly.LoadFile(path);

			Console.WriteLine("Scanning for plugins...");
			foreach (string dir in Directory.EnumerateDirectories("mods"))
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
					catch(Exception e)
					{
						Console.WriteLine(e);
					}
				}
				if (File.Exists(Path.Combine(dir, "fcs.def")))
				{
					defFiles.Add(Path.Combine(dir, "fcs.def"));
				}
			}

			Harmony harmony = new Harmony("FCS_extended");
			harmony.PatchAll();

			Console.WriteLine("Starting FCS...");
			object[] argsWrapped = new object[1];
			argsWrapped[0] = args;
			assembly.EntryPoint.Invoke(null, argsWrapped);
			
		}
		
		[HarmonyPatch("forgotten_construction_set.Definitions", "Load")]
		public class Definitions_Load_Patch
		{
			private static bool initialized = false;
			[HarmonyPrefix]
			static void Prefix(string filename, dynamic nav)
			{
				if(filename.EndsWith("/settings.def") && !initialized)
				{
					initialized = true;
					// load custom definitions
					Type baseDefinitions_type = assembly.GetType("forgotten_construction_set.Definitions");
					MethodInfo baseDefinitions_Load = AccessTools.Method("forgotten_construction_set.Definitions:Load");
					foreach (string defFile in defFiles)
					{
						Console.WriteLine(defFile);
						baseDefinitions_Load.Invoke(baseDefinitions_type, new object[] { defFile, nav });
					}
				}
				Console.WriteLine(filename, nav);
			}
		}
	}
}
