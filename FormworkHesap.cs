// ═══════════════════════════════════════════════════════════════
// Tekla Structures 2026 — Modeling Macro
// Formwork (Kalıp) Alan Hesabı — ORGANIZER UDA / KALIP_M2 FINAL
//
// KURULUM:
//   C:\ProgramData\Trimble\Tekla Structures\2026.0\environments\common\macros\modeling\
//   klasörüne kopyala.
//   Tekla'da: Tools → Macros → FormworkHesap_ORGANIZER → Run
//

// ═══ ORGANIZER AKTARIMI ═══
//   Bu makro Organizer'a doğrudan API ile veri basmaz; Organizer modeldeki
//   UDA alanlarını okur. Makro her beton parçaya aşağıdaki UDA'ları yazar:
//   FW_KALIP_M2, FW_KALIP_M2_TXT, FW_CONF, FW_RULE, FW_CAT,
//   FW_UPDATED, FW_ORG_KEY
//
//   objects.inp içine ek önerilen alanlar:
//   attribute("FW_REVIEW_TXT" "FW Kontrol Durumu" "string" "")
//   attribute("FW_UPDATED"    "FW Güncelleme"      "string" "")
//   attribute("FW_ORG_KEY"    "FW Organizer Key"   "string" "")
//
//   Organizer'da Property Template'e şu kolonları ekle:
//   USERDEFINED.FW_KALIP_M2
//   USERDEFINED.FW_KALIP_M2_TXT
//   USERDEFINED.FW_CAT
//   USERDEFINED.FW_RULE
//   USERDEFINED.FW_CONF
//   USERDEFINED.FW_UPDATED
// ═══════════════════════════════════════════════════════════════

// ═══ TEKNIK NOT ═══
//   Bu sürüm objects.inp dosyasını otomatik bozmaz; UDA tanımı ayrı kurulur.
//   CS0565 hatasına sebep olan dynamic kullanımını tamamen kaldırır.
//   Tekla.Structures.Solid namespace'ine doğrudan referans vermez.
//   TSM.Column kullanmaz.
// ═══════════════════════════════════════════════════════════════

using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

using Tekla.Structures.Model;
using TSM = Tekla.Structures.Model;
using Tekla.Structures.Geometry3d;

namespace Tekla.Technology.Akit.UserScript
{
    public class Script
    {
        public static void Run(Tekla.Technology.Akit.IScript akit)
        {
            try
            {
                FormworkMacro.Engine.Execute();
            }
            catch (Exception ex)
            {
                Console.WriteLine("KRITIK HATA: " + ex.Message);
            }
        }
    }
}

namespace FormworkMacro
{
    internal static class Cfg
    {
        public const double MM2_TO_M2         = 1e-6;
        public const double NORMAL_Z_THRESH   = 0.6;
        public const double MIN_FACE_AREA_MM2 = 10.0;

        // Varsayılan güvenli ayar: kotla SOG/radye yakalama kapalı.
        public const double SOG_ELEV_MM = double.NaN;
        public const double SOG_TOL_MM  = 300.0;

        public static readonly string[] CONCRETE_TOKENS = {
            "C20","C25","C30","C35","C40","C45","C50",
            "FC20","FC25","FC30","FC35","FC40",
            "B20","B25","B30","B35","B40",
            "CONCRETE","BETON","BETONARME","CONC","RC",
            // Tekla varsayılan beton malzemeleri
            "CONCRETE_UNDEFINED","CONCRETE_",
            // Ek Tekla malzeme adları
            "NORMAL_WEIGHT","LIGHTWEIGHT","C_","NW_CONCRETE"
        };

        public static readonly string[] SOG_KEYWORDS = {
            "RADYE","SLAB ON GRADE","SOG","GROUND SLAB",
            "TEMEL PLAK","FOUNDATION SLAB"
        };

        public static readonly CultureInfo TR = new CultureInfo("tr-TR");
    }

    internal class FaceData
    {
        public double Area;
        public Vector Normal;
    }

    internal class FwResult
    {
        public string PartId, PartClass, PartName, Profile, Material, Phase;
        public string Category, Rule, ConfLabel, ConfReasons;
        public string ReviewText, UpdatedAt, OrgKey;
        public double AreaM2, Lx, Ly, Lz, Confidence;
        public bool IsReview;
    }

    internal class ConfResult
    {
        public double Score;
        public string Label;
        public List<string> Reasons;
    }

    internal static class Engine
    {
        public static void Execute()
        {
            var model = new TSM.Model();
            if (!model.GetConnectionStatus())
            {
                Console.WriteLine("HATA: Tekla bağlantısı yok.");
                return;
            }

            var results    = new List<FwResult>();
            var debugMsgs  = new List<string>();
            var reviewList = new List<string>();
            var parts      = new List<TSM.Part>();

            bool hasSelection = false;
            var selector = new Tekla.Structures.Model.UI.ModelObjectSelector();
            var selected = selector.GetSelectedObjects();
            selected.Reset();
            while (selected.MoveNext())
            {
                TSM.Part p = selected.Current as TSM.Part;
                if (p != null)
                {
                    hasSelection = true;
                    if (IsConcrete(p)) parts.Add(p);
                }
            }

            if (!hasSelection)
            {
                var all = model.GetModelObjectSelector().GetAllObjects();
                all.Reset();
                while (all.MoveNext())
                {
                    TSM.Part p = all.Current as TSM.Part;
                    if (p != null && IsConcrete(p)) parts.Add(p);
                }
            }

            debugMsgs.Add(string.Format("Beton parça: {0} | seçim: {1}",
                parts.Count, hasSelection ? "EVET" : "HAYIR"));

            model.GetWorkPlaneHandler().SetCurrentTransformationPlane(new TransformationPlane());

            foreach (TSM.Part part in parts)
            {
                FwResult result = null;
                try
                {
                    result = Process(part, debugMsgs);
                }
                catch (Exception ex)
                {
                    string pid = "";
                    try { pid = part.Identifier.ID.ToString(); } catch { }
                    debugMsgs.Add(string.Format("Id={0}|EX:{1}", pid, ex.Message));
                }

                if (result == null) continue;
                results.Add(result);
                WriteUDA(part, result);

                if (result.IsReview)
                {
                    reviewList.Add(string.Format("REVIEW|Id={0}|m2={1}|conf={2}|{3}",
                        result.PartId,
                        result.AreaM2.ToString("F2", Cfg.TR),
                        result.Confidence.ToString("F2", Cfg.TR),
                        result.ConfReasons));
                }
            }

            model.CommitChanges();

            // NOT: objects.inp dosyasını makro içinden otomatik değiştirmiyoruz.
            // UDA tanımları ayrı verilen objects_FIXED.inp ile kurulmalıdır.
            debugMsgs.Add("objects.inp otomatik degistirilmedi. FW UDA'lari icin objects_FIXED.inp dosyasini kurup Tekla'yi yeniden baslatin.");

            string csv = ExportCsv(model, results, reviewList);
            debugMsgs.Add("CSV: " + csv);

            double total = 0.0;
            foreach (FwResult r in results) total += r.AreaM2;

            debugMsgs.Add(string.Format(Cfg.TR,
                "TOPLAM: {0:F2} m² | {1} parça | {2} REVIEW",
                total, results.Count, reviewList.Count));

            foreach (string msg in debugMsgs) Console.WriteLine(msg);
        }

        private static FwResult Process(TSM.Part part, List<string> debug)
        {
            string partId = "";
            try { partId = part.Identifier.ID.ToString(); } catch { }

            object solid = null;
            try { solid = part.GetSolid(); }
            catch (Exception ex)
            {
                debug.Add(string.Format("Id={0}|GetSolid FAIL:{1}", partId, ex.Message));
                return null;
            }
            if (solid == null)
            {
                debug.Add("Id=" + partId + "|solid null");
                return null;
            }

            Point min = GetPointProperty(solid, "MinimumPoint");
            Point max = GetPointProperty(solid, "MaximumPoint");
            if (min == null || max == null)
            {
                debug.Add("Id=" + partId + "|solid bbox okunamadı");
                return null;
            }

            double minZ = min.Z;
            double dx = Math.Abs(max.X - min.X);
            double dy = Math.Abs(max.Y - min.Y);
            double dz = Math.Abs(max.Z - min.Z);

            string cls      = GetRep(part, "CLASS");
            string partName = part.Name ?? "";
            string material = GetRep(part, "MATERIAL");
            string profile  = GetRep(part, "PROFILE");
            string phase    = GetPhase(part);

            double vA = 0, tA = 0, bA = 0;
            var vF = new List<FaceData>();
            var bF = new List<FaceData>();
            var tF = new List<FaceData>();
            ClassifyFaces(solid, ref vA, ref tA, ref bA, vF, bF, tF);

            string cat = ResolveCategory(part, cls, partName, dx, dy, dz);
            bool isSog = IsSog(part, partName, minZ);

            double fwArea;
            string rule;
            switch (cat)
            {
                case "COLUMN":
                    fwArea = vA; rule = "COLUMN(yan)"; break;
                case "BEAM":
                    fwArea = vA + bA; rule = "BEAM(yan+alt)"; break;
                case "FOUNDATION":
                    fwArea = vA; rule = "FOUNDATION(yan)"; break;
                case "FLOOR":
                    if (isSog) { fwArea = vA; rule = "FLOOR_SOG(yan)"; }
                    else       { fwArea = vA + bA; rule = "FLOOR(yan+alt)"; }
                    break;
                case "WALL":
                    fwArea = vA; rule = "WALL(all_v,cnt=" + vF.Count + ")"; break;
                case "CONTOURPLATE":
                    fwArea = vA + bA; rule = "CONTOURPLATE(yan+alt)"; break;
                default:
                    fwArea = vA + bA; rule = "FALLBACK(yan+alt)"; break;
            }

            bool edgeInflated = false;
            if ((cat == "FLOOR" || cat == "CONTOURPLATE") && vA > 1.0)
            {
                double est = 2.0 * (dx + dy) * dz;
                if (est > 1.0 && vA > est * 1.5)
                {
                    rule += " [WARN:edge_inflated]";
                    edgeInflated = true;
                }
            }

            double bboxVArea = 2.0 * Math.Max(dx * dz, dy * dz);
            ConfResult conf = CalcConf(cat, IsSloped(vF, bF), vF.Count, edgeInflated, bboxVArea);
            double areaM2 = fwArea * Cfg.MM2_TO_M2;

            debug.Add(string.Format(Cfg.TR,
                "Id={0}|{1}|m2={2:F2}|conf={3:F2}({4})|v/b/t={5}/{6}/{7}",
                partId, rule, areaM2, conf.Score, conf.Label, vF.Count, bF.Count, tF.Count));

            string updatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Cfg.TR);
            string reviewText = (conf.Score < 0.75 || cat == "WALL") ? "KONTROL" : "OK";
            string orgKey = "FW_" + partId;

            return new FwResult
            {
                PartId = partId,
                PartClass = cls,
                PartName = partName,
                Profile = profile,
                Material = material,
                Phase = phase,
                Category = cat,
                Rule = rule,
                AreaM2 = areaM2,
                Lx = dx / 1000.0,
                Ly = dy / 1000.0,
                Lz = dz / 1000.0,
                Confidence = conf.Score,
                ConfLabel = conf.Label,
                ConfReasons = string.Join(";", conf.Reasons.ToArray()),
                ReviewText = reviewText,
                UpdatedAt = updatedAt,
                OrgKey = orgKey,
                IsReview = conf.Score < 0.75 || cat == "WALL"
            };
        }

        private static void ClassifyFaces(
            object solid,
            ref double vA, ref double tA, ref double bA,
            List<FaceData> vF, List<FaceData> bF, List<FaceData> tF)
        {
            object fe = Call0(solid, "GetFaceEnumerator");
            if (fe == null) return;

            while (MoveNext(fe))
            {
                object face = GetProperty(fe, "Current");
                if (face == null) continue;

                double area;
                Vector n = FaceNormal(face, out area);
                if (area < Cfg.MIN_FACE_AREA_MM2 || n == null) continue;

                double nz = n.Z;
                var fd = new FaceData { Area = area, Normal = n };
                if (nz > Cfg.NORMAL_Z_THRESH)       { tA += area; tF.Add(fd); }
                else if (nz < -Cfg.NORMAL_Z_THRESH) { bA += area; bF.Add(fd); }
                else                                { vA += area; vF.Add(fd); }
            }
        }

        private static Vector FaceNormal(object face, out double area)
        {
            area = 0.0;
            double nx = 0.0, ny = 0.0, nz = 0.0, tw = 0.0;

            object le = Call0(face, "GetLoopEnumerator");
            if (le == null) return null;

            bool firstLoop = true;
            while (MoveNext(le))
            {
                object loop = GetProperty(le, "Current");
                if (loop == null) continue;

                object ve = Call0(loop, "GetVertexEnumerator");
                if (ve == null) continue;

                var pts = new List<Point>();
                while (MoveNext(ve))
                {
                    Point p = GetProperty(ve, "Current") as Point;
                    if (p != null) pts.Add(p);
                }

                if (pts.Count < 3) continue;

                Point p0 = pts[0];
                for (int i = 1; i < pts.Count - 1; i++)
                {
                    double ax = pts[i].X - p0.X;
                    double ay = pts[i].Y - p0.Y;
                    double az = pts[i].Z - p0.Z;
                    double bx = pts[i + 1].X - p0.X;
                    double by = pts[i + 1].Y - p0.Y;
                    double bz = pts[i + 1].Z - p0.Z;

                    double cx = ay * bz - az * by;
                    double cy = az * bx - ax * bz;
                    double cz = ax * by - ay * bx;

                    double mag = Math.Sqrt(cx * cx + cy * cy + cz * cz);
                    if (mag < 1e-6) continue;

                    double ta = 0.5 * mag;
                    if (firstLoop)
                    {
                        area += ta;
                        nx += (cx / mag) * ta;
                        ny += (cy / mag) * ta;
                        nz += (cz / mag) * ta;
                        tw += ta;
                    }
                    else
                    {
                        area -= ta;
                    }
                }

                firstLoop = false;
            }

            area = Math.Abs(area);
            if (tw < 1e-9) return null;
            double len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (len < 1e-9) return null;
            return new Vector(nx / len, ny / len, nz / len);
        }

        private static string ResolveCategory(TSM.Part part, string cls, string name,
            double dx, double dy, double dz)
        {
            string c = (cls ?? "").ToUpperInvariant().Trim();
            string nm = (name ?? "").ToUpperInvariant().Trim();

            // Tekla API'de TSM.Slab sinifi yoktur; slab/doseme genelde ContourPlate veya Beam turevi gelir.
            // Bu nedenle sadece isim/class/profile/geometri ile yakaliyoruz.
            if (nm == "SLAB" || nm.Contains("SLAB") || nm.Contains("DÖŞEME") || nm.Contains("DOSEME") || nm.Contains("RADYE") ||
                c.Contains("SLAB") || c.Contains("FLOOR") || c.Contains("DÖŞEME") || c.Contains("DOSEME"))
                return "FLOOR";

            if (c == "1" || c.Contains("COLUMN") || c.Contains("KOLON") ||
                nm.Contains("COLUMN") || nm.Contains("KOLON"))
                return "COLUMN";

            if (c.Contains("WALL") || c.Contains("PERDE") ||
                nm.Contains("WALL") || nm.Contains("PERDE"))
                return "WALL";

            if (part is TSM.Beam)
            {
                double maxPlan = Math.Max(dx, dy);
                double minPlan = Math.Min(dx, dy);

                if (dz > 1500.0 && minPlan < 600.0 && maxPlan > dz * 0.5)
                    return "WALL";

                if (dz > 1500.0 && maxPlan < 1200.0)
                    return "COLUMN";

                return "BEAM";
            }

            if (part is TSM.ContourPlate)
            {
                // Döşeme/slab olarak tanınan isimler
                string[] floorKws = { "SLAB", "DOSEME", "DÖŞEME", "FLOOR", "PLAK", "RADYE" };
                foreach (string kw in floorKws)
                    if (nm.Contains(kw) || c.Contains(kw)) return "FLOOR";

                // Perde/duvar olarak tanınan isimler
                string[] wallKws = { "WALL", "PERDE", "SHEAR" };
                foreach (string kw in wallKws)
                    if (nm.Contains(kw) || c.Contains(kw)) return "WALL";

                // Panel / plaka → geometriye göre karar ver
                // dz küçük (ince) → FLOOR, dz büyük → WALL
                if (dz < Math.Min(dx, dy) * 0.5)
                    return "FLOOR";

                return "CONTOURPLATE";
            }

            if (c == "2" || c.Contains("BEAM") || c.Contains("KIRIŞ")) return "BEAM";
            if (c.Contains("SLAB") || c.Contains("FLOOR") || c.Contains("DÖŞEME") ||
                nm.Contains("SLAB") || nm.Contains("DÖŞEME") || nm.Contains("DOSEME")) return "FLOOR";
            if (c.Contains("FOUND") || c.Contains("TEMEL") || c.Contains("FOOTING") ||
                nm.Contains("TEMEL") || nm.Contains("FOOTING")) return "FOUNDATION";

            return "UNKNOWN";
        }

        private static bool IsConcrete(TSM.Part part)
        {
            string mat = GetRep(part, "MATERIAL").ToUpperInvariant().Trim();
            if (mat.Length == 0) return false;

            // "Concrete_Undefined", "CONCRETE_S1" gibi Tekla varsayılan malzemeleri yakala
            if (mat.Contains("CONCRETE") || mat.Contains("BETON")) return true;

            foreach (string token in Cfg.CONCRETE_TOKENS)
            {
                if (mat == token ||
                    mat.StartsWith(token + " ") || mat.StartsWith(token + "/") ||
                    mat.StartsWith(token + "-") || mat.StartsWith(token + "_") || mat.StartsWith(token + ".") ||
                    mat.Contains(" " + token) || mat.Contains("/" + token) ||
                    mat.Contains("-" + token) || mat.Contains("_" + token) || mat.Contains("." + token))
                    return true;
            }
            return false;
        }

        private static bool IsSog(TSM.Part part, string name, double bottomZMm)
        {
            string nm = (name ?? "").ToUpperInvariant();
            // Tekla API'de TSM.Slab sinifi yoktur; slab/doseme tespiti isim/class/report property ile yapilir.
            string cls = GetRep(part, "CLASS").ToUpperInvariant().Trim();
            string type = GetRep(part, "TYPE").ToUpperInvariant().Trim();
            string prof = GetRep(part, "PROFILE").ToUpperInvariant().Trim();

            if (nm == "SLAB" || nm.Contains("SLAB") || nm.Contains("DÖŞEME") || nm.Contains("DOSEME") || nm.Contains("RADYE") ||
                cls.Contains("SLAB") || cls.Contains("FLOOR") || cls.Contains("DÖŞEME") || cls.Contains("DOSEME") ||
                type.Contains("SLAB") || type.Contains("CONTOUR") ||
                prof.Contains("SLAB") || prof.Contains("DÖŞEME") || prof.Contains("DOSEME"))
                return true;
            foreach (string kw in Cfg.SOG_KEYWORDS)
                if (nm.Contains(kw)) return true;
            if (!double.IsNaN(Cfg.SOG_ELEV_MM))
                if (Math.Abs(bottomZMm - Cfg.SOG_ELEV_MM) <= Cfg.SOG_TOL_MM) return true;
            return false;
        }

        private static bool IsSloped(List<FaceData> vF, List<FaceData> bF)
        {
            foreach (FaceData f in vF)
                if (f.Normal != null && Math.Abs(f.Normal.Z) > 0.05) return true;
            foreach (FaceData f in bF)
                if (f.Normal != null && Math.Abs(f.Normal.Z) < 0.98) return true;
            return false;
        }

        private static string GetRep(TSM.Part p, string prop)
        {
            try { string v = ""; p.GetReportProperty(prop, ref v); return v ?? ""; }
            catch { return ""; }
        }

        private static string GetPhase(TSM.Part part)
        {
            try
            {
                var ph = new TSM.Phase();
                part.GetPhase(out ph);
                return ph != null ? ph.PhaseNumber.ToString() : "";
            }
            catch { return ""; }
        }

        private static void WriteUDA(TSM.Part part, FwResult r)
        {
            // CSV'deki Alan_m2 değeri ile birebir aynı kalıp / beton gören yüz alanı.
            // FW_AREA_M2 eski isim olarak da yazılır; Organizer'da asıl kullanılacak alan FW_KALIP_M2'dir.
            string areaTxt = r.AreaM2.ToString("F3", Cfg.TR);
            try { part.SetUserProperty("FW_KALIP_M2", r.AreaM2); } catch { }
            try { part.SetUserProperty("FW_KALIP_M2_TXT", areaTxt); } catch { }
            try { part.SetUserProperty("FW_AREA_M2", r.AreaM2); } catch { }

            try { part.SetUserProperty("FW_CONF", r.Confidence); } catch { }
            try { part.SetUserProperty("FW_RULE", r.Rule); } catch { }
            try { part.SetUserProperty("FW_CAT", r.Category); } catch { }
            try { part.SetUserProperty("FW_UPDATED", r.UpdatedAt ?? ""); } catch { }
            try { part.SetUserProperty("FW_ORG_KEY", r.OrgKey ?? ""); } catch { }
            try { part.Modify(); } catch { }
        }

        // ── objects.inp UDA tanımları — part bloğu içine ────
        // Tekla'nın beklediği format: part bloğu içinde attribute tanımı
        private static List<string> WriteObjectsInp(TSM.Model model)
        {
            var log = new List<string>();

            // Olası objects.inp konumları — öncelik sırasıyla dene
            var candidates = new List<string>();
            try
            {
                // 1) Model klasörü
                candidates.Add(Path.Combine(model.GetInfo().ModelPath, "objects.inp"));
                // 2) Tekla common environment
                string pdata = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                string[] dirs = Directory.GetDirectories(
                    Path.Combine(pdata, "Trimble", "Tekla Structures"),
                    "*.0", SearchOption.TopDirectoryOnly);
                if (dirs.Length > 0)
                {
                    Array.Sort(dirs);
                    string ver = dirs[dirs.Length - 1]; // en son versiyon
                    candidates.Add(Path.Combine(ver, "Environments", "common", "objects.inp"));
                }
            }
            catch { }

            string fwBlock =
"\r\n// === FormworkHesap FW UDA'lari ===\r\n" +
"part(\"FW_Properties\",\"FW Properties\",1)\r\n" +
"{\r\n" +
" attribute(\"FW_AREA_M2\",\"FW_area_m2\",double,\"%f\",no,none,\"0.0\",\"0.0\")\r\n" +
" {\r\n  value(0.0,0)\r\n }\r\n" +
" attribute(\"FW_CONF\",\"FW_conf\",double,\"%f\",no,none,\"0.0\",\"0.0\")\r\n" +
" {\r\n  value(0.0,0)\r\n }\r\n" +
" attribute(\"FW_RULE\",\"FW_rule\",string,\"%s\",no,none,\"0.0\",\"0.0\")\r\n" +
" {\r\n  value(\"\",0)\r\n }\r\n" +
" attribute(\"FW_CAT\",\"FW_cat\",string,\"%s\",no,none,\"0.0\",\"0.0\")\r\n" +
" {\r\n  value(\"\",0)\r\n }\r\n" +
" attribute(\"FW_REVIEW\",\"FW_review\",integer,\"%d\",no,none,\"0.0\",\"0.0\")\r\n" +
" {\r\n  value(0,0)\r\n }\r\n" +
" attribute(\"FW_REVIEW_TXT\",\"FW_review_txt\",string,\"%s\",no,none,\"0.0\",\"0.0\")\r\n" +
" {\r\n  value(\"\",0)\r\n }\r\n" +
" attribute(\"FW_UPDATED\",\"FW_updated\",string,\"%s\",no,none,\"0.0\",\"0.0\")\r\n" +
" {\r\n  value(\"\",0)\r\n }\r\n" +
" attribute(\"FW_ORG_KEY\",\"FW_org_key\",string,\"%s\",no,none,\"0.0\",\"0.0\")\r\n" +
" {\r\n  value(\"\",0)\r\n }\r\n" +
" modify(1)\r\n" +
"}\r\n";

            foreach (string path in candidates)
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    string content = File.ReadAllText(path, Encoding.UTF8);
                    if (content.Contains("FW_AREA_M2") && content.Contains("FW_Properties"))
                    { log.Add("objects.inp: FW blogu zaten var: " + path); return log; }

                    // Eski düz satırları temizle
                    var lines = new List<string>(content.Split('\n'));
                    var clean = new List<string>();
                    foreach (var line in lines)
                    {
                        if (line.Contains("FormworkHesap") || line.Contains("FW_AREA_M2") ||
                            line.Contains("FW_CONF") || line.Contains("FW_RULE") ||
                            line.Contains("FW_CAT") || line.Contains("FW_REVIEW") ||
                            line.Contains("FW_UPDATED") || line.Contains("FW_ORG_KEY"))
                            continue;
                        clean.Add(line);
                    }
                    string newContent = string.Join("\n", clean).TrimEnd() + fwBlock;
                    File.WriteAllText(path, newContent, Encoding.UTF8);
                    log.Add("objects.inp guncellendi: " + path);
                    log.Add("!! Tekla'yi yeniden baslatip makroyu tekrar calistir !!");
                    return log;
                }
                catch (Exception ex)
                {
                    log.Add("objects.inp HATA (" + path + "): " + ex.Message);
                }
            }
            log.Add("objects.inp bulunamadi — elle ekle");
            return log;
        }

        private static string ExportCsv(TSM.Model model, List<FwResult> results, List<string> reviewList)
        {
            string folder = model.GetInfo().ModelPath;
            string csvPath = Path.Combine(folder, "formwork_report.csv");
            var sb = new StringBuilder();
            sb.Append('\uFEFF');
            sb.AppendLine("PartId;Kategori;Kural;Alan_m2;FW_KALIP_M2;Lx_m;Ly_m;Lz_m;Confidence;ConfEtiket;ConfNeden;UpdatedAt;OrgKey;PartName;Profil;Malzeme;Faz");

            foreach (FwResult r in results)
            {
                sb.AppendLine(string.Format(Cfg.TR,
                    "{0};{1};{2};{3:F3};{3:F3};{4:F3};{5:F3};{6:F3};{7:F2};{8};{9};{10};{11};{12};{13};{14};{15};{16}",
                    r.PartId, r.Category, r.Rule, r.AreaM2, r.Lx, r.Ly, r.Lz,
                    r.Confidence, r.ConfLabel, r.ConfReasons.Replace(";", "|"),
                    r.UpdatedAt, r.OrgKey,
                    r.PartName, r.Profile, r.Material, r.Phase));
            }

            if (reviewList.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("--- REVIEW LIST (conf < 0,75 veya WALL) ---");
                foreach (string line in reviewList) sb.AppendLine(line);
            }

            File.WriteAllText(csvPath, sb.ToString(), Encoding.UTF8);
            return csvPath;
        }

        private static ConfResult CalcConf(string cat, bool isSloped, int vFaceCount,
            bool edgeInflated, double bboxVAreaMm2)
        {
            var bases = new Dictionary<string, double>();
            bases["COLUMN"] = 0.95;
            bases["BEAM"] = 0.92;
            bases["FOUNDATION"] = 0.92;
            bases["FLOOR"] = 0.88;
            bases["WALL"] = 0.75;
            bases["CONTOURPLATE"] = 0.85;
            bases["UNKNOWN"] = 0.60;

            double score = bases.ContainsKey(cat) ? bases[cat] : 0.60;
            var reasons = new List<string>();

            if (isSloped) { score -= 0.10; reasons.Add("sloped_element"); }

            if (cat == "WALL" && vFaceCount > 0 && bboxVAreaMm2 > 1.0)
            {
                double d = vFaceCount / (bboxVAreaMm2 * Cfg.MM2_TO_M2);
                if (d > 10) { score -= 0.12; reasons.Add(string.Format(Cfg.TR, "wall_high_density:{0:F1}/m2", d)); }
                else if (d > 5) { score -= 0.06; reasons.Add(string.Format(Cfg.TR, "wall_med_density:{0:F1}/m2", d)); }
            }

            if (edgeInflated) { score -= 0.08; reasons.Add("edge_inflated"); }

            score = Math.Max(0.0, Math.Min(1.0, Math.Round(score, 2)));
            string label = score >= 0.90 ? "HIGH" : score >= 0.75 ? "MEDIUM" : score >= 0.55 ? "LOW" : "REVIEW";
            return new ConfResult { Score = score, Label = label, Reasons = reasons };
        }

        private static object Call0(object obj, string method)
        {
            if (obj == null) return null;
            MethodInfo mi = obj.GetType().GetMethod(method, Type.EmptyTypes);
            if (mi == null) return null;
            return mi.Invoke(obj, null);
        }

        private static bool MoveNext(object enumerator)
        {
            object val = Call0(enumerator, "MoveNext");
            return val is bool && (bool)val;
        }

        private static object GetProperty(object obj, string prop)
        {
            if (obj == null) return null;
            PropertyInfo pi = obj.GetType().GetProperty(prop);
            if (pi == null) return null;
            return pi.GetValue(obj, null);
        }

        private static Point GetPointProperty(object obj, string prop)
        {
            return GetProperty(obj, prop) as Point;
        }
    }
}