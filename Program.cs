using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace EfRenamer
{

	public class Settings 
	{
		public static bool WaitExit { get; set; }
		public static bool UseDefaultNamer { get; set; }
		public static bool CaseInsensitive { get; set; }
		public static string File { get; set; }
		public static IDictionary<string, string> NameMapper { get; set; }
		public static IDictionary<string, string> PartNameMapper { get; set; }
		public static HashSet<string> Entities { get; set; }
		public static HashSet<string> EntitiesPrefix { get; set; }
	}

	public class CaseInsesitiveComparer : IEqualityComparer<string>
	{

		#region IEqualityComparer<string> Members

		public bool Equals(string x, string y)
		{
			if (Settings.CaseInsensitive)
			{
				return (string.Compare(x, y, true) == 0);
			}
			return string.Compare(x, y, false) == 0;
		}

		public int GetHashCode(string obj)
		{
			if (obj == null) return 0;
			return obj.ToLowerInvariant().GetHashCode();
		}

		#endregion
	}//EOIC

	class Program
	{
		private static System.Data.Entity.Design.PluralizationServices.PluralizationService NameService = System.Data.Entity.Design.PluralizationServices.PluralizationService.CreateService(CultureInfo.GetCultureInfo("en-us"));

		static void Main(string[] args)
		{

			try
			{
				if ((args == null) || (args.Length <= 0)) throw new ApplicationException("Args");

				Settings.UseDefaultNamer = true;
				Settings.CaseInsensitive = false;
				Settings.Entities = new HashSet<string>();
				Settings.EntitiesPrefix = new HashSet<string>();
				for(var i = 0; i < args.Length; i++)
				{
					var a = args[i];
					if (!a.StartsWith("-")) throw new ApplicationException("Invalid " + a);
					switch (a.ToLowerInvariant().Substring(1)) 
					{
						case "t":
							i++;
							Settings.Entities.Add(args[i]);
							break;
						case "tp":
							i++;
							Settings.EntitiesPrefix.Add(args[i]);
							break;
						case "e": Settings.WaitExit = true; break;
						case "d": Settings.UseDefaultNamer = false; break;
						case "i": Settings.CaseInsensitive = true; break;
						case "m":
							i++;
							Settings.NameMapper = GetCustomNameMapper(args[i]);
							break;
						case "p":
							i++;
							Settings.PartNameMapper = GetCustomNameMapper(args[i]);
							break;
						case "f":
							i++;
							Settings.File = Path.GetFullPath(args[i]);
							break;
						default:
							throw new ApplicationException("Invalid " + a);
					}
				}

				var now = DateTime.Now.ToString(".yyyyMMdd-HHmmss");
				File.Copy(Settings.File, Settings.File + now);
				var xd = XDocument.Load(Settings.File);
				var ns = xd.Root.Name.Namespace;
				XNamespace sns = "http://schemas.microsoft.com/ado/2009/11/edm";
				XNamespace mns = "http://schemas.microsoft.com/ado/2009/11/mapping/cs";

				var e = xd.Root
					.Element(ns + "Runtime")
					.Element(ns + "ConceptualModels")
					.Element(sns + "Schema");

				var codeNameSpace = e.Attribute("Namespace").Value;
				foreach (var container in e.Elements(sns + "EntityContainer")) 
				{
					foreach (var entitySet in container.Elements(sns + "EntitySet"))
					{
						if (!CanProcess(entitySet)) continue;
						MapAttributeClassPlural(entitySet, "Name");
						var n = entitySet.Attribute("EntityType").Value.Substring(codeNameSpace.Length + 1);
						entitySet.Attribute("EntityType").SetValue(codeNameSpace + "." + MapName(n, true));
					}
				}

				foreach (var entityType in e.Elements(sns + "EntityType"))
				{
					if (!CanProcess(entityType)) continue;

					MapAttributeClass(entityType);
					if(entityType.Element(sns + "Key") != null)
					{
						foreach (var pref in entityType.Element(sns + "Key").Elements(sns + "PropertyRef"))
						{
							MapAttribute(pref);
						}
					}
					foreach (var prop in entityType.Elements(sns + "Property"))
					{
						MapAttribute(prop);
					}
				}

				e = xd.Root
					.Element(ns + "Runtime")
					.Element(ns + "Mappings");
				foreach (var m in e.Elements(mns + "Mapping"))
				{
					foreach (var ecm in m.Elements(mns + "EntityContainerMapping"))
					{
						foreach (var esm in ecm.Elements(mns + "EntitySetMapping"))
						{
							if (!CanProcess(esm)) continue;
							MapAttributeClassPlural(esm);
							foreach (var etm in esm.Elements(mns + "EntityTypeMapping"))
							{
								var esmName = MapName(etm.Attribute("TypeName").Value.Substring(codeNameSpace.Length + 1), true);
								etm.Attribute("TypeName").SetValue(codeNameSpace + "." + esmName);
								foreach (var sp in etm.Element(mns + "MappingFragment").Elements(mns + "ScalarProperty"))
								{
									MapAttribute(sp);
								}
							}
						}
					}
				}

				xd.Save(Settings.File); // + ".modified");

				var df = Settings.File + ".diagram";
				if (File.Exists(df))
				{
					File.Copy(df, df + now);
					xd = XDocument.Load(df);
					var dns = xd.Root.Name.Namespace;
					e = xd.Root.Element(dns + "Designer")
						.Element(dns + "Diagrams");
					foreach (var dia in e.Elements(dns + "Diagram"))
					{
						foreach (var es in dia.Elements(dns + "EntityTypeShape"))
						{
							var n = es.Attribute("EntityType").Value.Substring(codeNameSpace.Length + 1);
							if (!CanProcess(n)) continue;
							es.Attribute("EntityType").SetValue(codeNameSpace + "." + MapName(n, true));
						}
					}
					xd.Save(df); // + ".modified");
				}
				else 
				{
					Console.WriteLine("# Cannot find: " + df);
				}

			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.Message + ex.StackTrace);
                Console.WriteLine("Usage: efrenamer -f path.edmx [-m nameMappingFile] [-p namePartMappingFile] [-i] [-d] [-t] [-tp]");
                Console.WriteLine(" -f model.edmx          : file to use. Unload the VS project first, run this command, then reload");
                Console.WriteLine(" -m nameMappingFile     : a file with a name=value lines that specify how a full name is mapped to value, if not specified, or if something is not found, then the default built-in name mapper is used");
                Console.WriteLine(" -p namePartMappingFile : a file with a namepart=value lines that specify how a name part (separated by _) is mapped to value, if not specified, or if something is not found, then the default built-in name mapper is used");
                Console.WriteLine(" -i                     : use case insensitive for -m, -p");
                Console.WriteLine(" -d                     : do not use default built-in name mapper, by default when nothing better is found, an automatic default name mapper is used");
                Console.WriteLine(" -t entityName          : consider only entityName entity, can be repeated as needed, use * for all");
                Console.WriteLine(" -tp entityNamePrefix   : consider only entities whose name starts with entityNamePrefix, can be repeated as needed");
                Console.WriteLine(" -e                     : wait to press Enter on exit, useful to debug in Visual Studio");
			}
			if (Settings.WaitExit)
			{
				Console.WriteLine("Press Enter key to continue ...");
				Console.ReadLine();
			}
		}

		private static bool CanProcess(XElement e, string attribute = "Name") 
		{
			var n = e.Attribute(attribute).Value;
			return CanProcess(n);
		}

		private static bool CanProcess(string n)
		{
			if (Settings.Entities.Contains("*") 
				|| Settings.EntitiesPrefix.Contains("*")) return true;
			foreach (var p in Settings.EntitiesPrefix) 
			{
				if (n.StartsWith(p)) 
				{
					return true;
				}
			}
			return Settings.Entities.Contains(n);
		}

		private static string MapAttributeClassPlural(XElement e, string attribute = "Name")
		{
			if (e == null) return null;
			var n = e.Attribute(attribute).Value;
			n = MapNamePlural(n);
			e.Attribute(attribute).SetValue(n);
			return n;
		}

		private static string MapNamePlural(string n)
		{
			n = MapName(n, true);
			if (!NameService.IsPlural(n))
			{
				n = NameService.Pluralize(n);
			}
			return n;
		}

		private static string MapAttributeClass(XElement e, string attribute = "Name")
		{
			return MapAttribute(e, attribute, true);
		}

		private static string MapAttribute(XElement e, string attribute = "Name", bool isClass = false)
		{
			if (e == null) return null;
			var n = MapName(e.Attribute(attribute).Value, isClass);
			e.Attribute(attribute).SetValue(n);
			return n;
		}

		private static Dictionary<string, string> Cache = new Dictionary<string, string>(new CaseInsesitiveComparer());

		private static void AddToCache(string n, bool isClass)
		{
			var cacheKey = (isClass ? "*" : "-") + n;
			if (!Cache.ContainsKey(cacheKey)) Cache.Add(cacheKey, n);
		}

		private static string MapName(string n, bool isClass) 
		{
			//if (n.IndexOf('_') < 0) return n;

			var cacheKey = (isClass ? "*" : "-") + n;
			if (!Cache.ContainsKey(cacheKey)) 
			{
				var m = n;
				if ((Settings.NameMapper != null) && Settings.NameMapper.ContainsKey(n))
				{
					m = Settings.NameMapper[n];
				}
				else if (Settings.PartNameMapper != null) 
				{
					var parts = n.Split('_');
					var sb = new StringBuilder();
					foreach (var p in parts) 
					{
						if (Settings.PartNameMapper.ContainsKey(p))
						{
							sb.Append(Settings.PartNameMapper[p]);
						}
						else 
						{
							if (Settings.UseDefaultNamer)
							{
								sb.Append(DefaultMapName(p));
							}
						}
					}
					m = sb.ToString();
				}
				else
				{
					if (Settings.UseDefaultNamer)
					{
						m = DefaultMapName(n);
					}
				}
				Console.WriteLine(n + " => " + m);
				Cache.Add(cacheKey, m);
			}
			return Cache[cacheKey];
		}

		private static string DefaultMapName(string name)
		{
			var res = new StringBuilder();
			for (var i = 0; i < name.Length; i++)
			{
				var previous = i - 1;
				if (Char.IsLetterOrDigit(name[i]))
				{
					if ((previous < 0) || ((previous >= 0) && !Char.IsLetterOrDigit(name[previous])))
					{
						res.Append(Char.ToUpperInvariant(name[i]));
					}
					else
					{
						if ((previous >= 0) && Char.IsUpper(name[i]) && Char.IsLower(name[previous]))
						{
							res.Append(Char.ToUpperInvariant(name[i]));
						}
						else
						{
							res.Append(Char.ToLowerInvariant(name[i]));
						}
					}
				}
			}
			return res.ToString();
		}

		private static Dictionary<string, string> GetCustomNameMapper(string nmf)
		{
			var customNameMapper = new Dictionary<string, string>(new CaseInsesitiveComparer());
			if (!string.IsNullOrEmpty(nmf))
			{
				using (var reader = new StreamReader(nmf))
				{
					for (var line = reader.ReadLine(); line != null; line = reader.ReadLine())
					{
						if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
						var parts = line.Split(new[] { '=' });
						if (parts.Length != 2)
						{
							Console.Error.WriteLine("#Namemapper: Incorrect: " + line + " File: " + nmf);
						}
						parts[0] = parts[0].Trim();
						parts[1] = parts[1].Trim();
						if (customNameMapper.ContainsKey(parts[0]))
						{
							Console.Error.WriteLine("#Namemapper: Repeated: " + line + " File: " +  nmf);
						}
						else
						{
							customNameMapper.Add(parts[0], parts[1]);
						}
					}
				}
			}
			return customNameMapper;
		}
	}
}
