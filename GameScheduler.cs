using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Diagnostics;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Web.Script.Serialization;
using Microsoft.Win32;

public class Settings {
 public string BetterGI = "";
 public string March7th = "";
 public string Dragon = "默认配置", RailTask = "main";
 public bool GenshinEnabled = false, RailEnabled = false, Wake = false;
 public string GenshinTime = "04:10", RailTime = "06:10", Mode = "每天", OnceDate = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd");
 public int Days = 127, TimeoutMinutes = 180;
 public bool RailFirst = false;
 public bool UseBlackScreen = true;
}

static class App {
 public static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
 public static readonly string Exe = Application.ExecutablePath;
 public static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
 public static string Config { get { return Path.Combine(Root,"settings.json"); } }
 public static readonly string Id = "GameScheduler_" + BitConverter.ToString(System.Security.Cryptography.SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(Root.ToLowerInvariant()))).Replace("-","").Substring(0,12);
 public static readonly string RunMutex = @"Global\WZY_GameAutomation_Runner";
 public static readonly string StopEvent = @"Global\WZY_GameAutomation_Stop";
 public static Settings Load() { return File.Exists(Config) ? Json.Deserialize<Settings>(File.ReadAllText(Config,Encoding.UTF8)) : new Settings(); }
 public static void Save(Settings s) { Validate(s); Atomic(Config,Json.Serialize(s)); }
 public static void Atomic(string file,string text) { string tmp=file+".tmp"; File.WriteAllText(tmp,text,new UTF8Encoding(true)); if(File.Exists(file)) File.Replace(tmp,file,null); else File.Move(tmp,file); }
 public static void Log(string text) { Directory.CreateDirectory(Path.Combine(Root,"logs")); string p=Path.Combine(Root,"logs",DateTime.Now.ToString("yyyy-MM-dd")+".log"); lock(Json) File.AppendAllText(p,DateTime.Now.ToString("HH:mm:ss")+" "+text+Environment.NewLine,Encoding.UTF8); }
 public static bool Admin { get { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); } }
 public static string Quote(string s) {
  var b=new StringBuilder("\"");int slashes=0;
  foreach(char c in s){if(c=='\\'){slashes++;continue;}if(c=='\"'){b.Append('\\',slashes*2+1);b.Append(c);}else{b.Append('\\',slashes);b.Append(c);}slashes=0;}
  b.Append('\\',slashes*2);b.Append('"');return b.ToString();
 }
 public static void Validate(Settings s) {
  if(!File.Exists(s.BetterGI)) throw new Exception("找不到 BetterGI，请重新选择路径。");
  if(!File.Exists(s.March7th) || !File.Exists(RailExe(s))) throw new Exception("找不到三月七启动器或 March7th Assistant.exe，请选择完整安装目录中的启动器。");
  DateTime dt; if(!DateTime.TryParseExact(s.GenshinTime,"HH:mm",null,System.Globalization.DateTimeStyles.None,out dt)||!DateTime.TryParseExact(s.RailTime,"HH:mm",null,System.Globalization.DateTimeStyles.None,out dt)) throw new Exception("时间格式必须是 HH:mm。");
  if(s.TimeoutMinutes<1 || s.TimeoutMinutes>720) throw new Exception("超时时间必须在 1～720 分钟之间。");
  if(s.Mode!="每天" && s.Mode!="每周" && s.Mode!="仅一次") throw new Exception("计划类型无效。");
  if(s.Mode=="每周" && s.Days==0) throw new Exception("请至少选择一个星期。");
  if(!DateTime.TryParseExact(s.OnceDate,"yyyy-MM-dd",null,System.Globalization.DateTimeStyles.None,out dt)) throw new Exception("单次日期无效。");
  if(!new[]{"main","routine","daily","power","universe","currencywars","divergent"}.Contains(s.RailTask)) throw new Exception("崩铁任务无效。");
  DragonPath(s);
  if(s.UseBlackScreen && !File.Exists(Path.Combine(Root,"BlackScreenMute.Game.exe"))) throw new Exception("找不到游戏兼容版黑屏静音程序，请把 BlackScreenMute.Game.exe 与定时助手放在一起。");
 }
 public static string RailExe(Settings s) { return Path.Combine(Path.GetDirectoryName(s.March7th),"March7th Assistant.exe"); }
 public static string DragonPath(Settings s) {
  string dir=Path.Combine(Path.GetDirectoryName(s.BetterGI),@"User\OneDragon");
  if(Directory.Exists(dir)) foreach(string f in Directory.GetFiles(dir,"*.json")) {
   var d=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(f)); object name;
   if(d.TryGetValue("Name",out name) && (string)name==s.Dragon) return f;
  }
  throw new Exception("找不到选中的原神一条龙配置，请打开 BetterGI 创建并保存配置。");
 }
 public static string Precheck(Settings s) {
  Validate(s); var d=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(DragonPath(s))); object a;
  string action=d.TryGetValue("CompletionAction",out a)?Convert.ToString(a):"无";
  var lines=new List<string>(); lines.Add("BetterGI："+FileVersionInfo.GetVersionInfo(s.BetterGI).ProductVersion+"；一条龙："+s.Dragon);
  lines.Add("原神完成后操作："+action+"（定时原神要求“关闭游戏和软件”）");
  object enabled, defs;
  if(d.TryGetValue("TaskEnabledList",out enabled)&&d.TryGetValue("TaskDefinitions",out defs)) {
   var e=(Dictionary<string,object>)enabled;var n=(Dictionary<string,object>)defs;
   lines.Add("原神已启用："+string.Join("、", e.Where(k=>Convert.ToBoolean(k.Value)&&n.ContainsKey(k.Key)).Select(k=>Convert.ToString(n[k.Key])).ToArray()));
  }
  string yaml=Path.Combine(Path.GetDirectoryName(s.March7th),"config.yaml");
  if(File.Exists(yaml)) foreach(string key in new[]{"game_path","use_fuel","use_reserved_trailblaze_power","after_finish","instance_type","power_enable"}) {
   string l=File.ReadLines(yaml).FirstOrDefault(x=>x.StartsWith(key+":")); if(l!=null) lines.Add("崩铁 "+l.Split('#')[0].Trim());
  }
  lines.Add("计划仅在 Windows 已登录且桌面未锁定时运行；默认不补跑错过的时间。");
  lines.Add(s.UseBlackScreen?"任务开始时自动黑屏静音；手动按 Esc 退出黑屏并恢复声音。":"已关闭任务开始时黑屏静音。");
  return string.Join(Environment.NewLine,lines.ToArray());
 }
 public static void CheckDragon(Settings s) {
  var d=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(DragonPath(s))); object a;
  if(!d.TryGetValue("CompletionAction",out a)||Convert.ToString(a)!="关闭游戏和软件") throw new Exception("请在 BetterGI 的一条龙中把完成后操作设为“关闭游戏和软件”，保存后再运行，确保完成后释放桌面。");
  object next; if(d.TryGetValue("NextTaskId",out next)&&!string.IsNullOrEmpty(Convert.ToString(next))) throw new Exception("原神配置有“从此执行”标记，请先在 BetterGI 清除，避免跳过日常任务。");
 }
 public static Process Elevated(string args,bool wait) {
  var p=Process.Start(new ProcessStartInfo(Exe,args){UseShellExecute=true,Verb=Admin?"open":"runas",WorkingDirectory=Root});
  if(wait) { p.WaitForExit(); if(p.ExitCode!=0) throw new Exception("操作未完成，请查看运行记录中的原因。"); }
  return p;
 }
 [DllImport("user32.dll",SetLastError=true)] static extern IntPtr OpenInputDesktop(uint flags,bool inherit,uint access);
 [DllImport("user32.dll")] static extern bool SwitchDesktop(IntPtr desk);
 [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr desk);
 [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint flags);
 public static bool Unlocked() { IntPtr d=OpenInputDesktop(0,false,0x100); if(d==IntPtr.Zero)return false; try{return SwitchDesktop(d);}finally{CloseDesktop(d);} }
 public static void StopOwned(Process p) {
  if(p==null)return; try { if(!p.HasExited) {var k=Process.Start(new ProcessStartInfo("taskkill.exe","/PID "+p.Id+" /T /F"){UseShellExecute=false,CreateNoWindow=true}); k.WaitForExit(10000); if(!p.WaitForExit(5000))throw new Exception("助手未能停止。");} } catch(Exception e){Log("停止进程失败："+e.Message);throw;}
 }
 public static int WaitChild(Process p,EventWaitHandle stop,TimeSpan timeout) {
  Stopwatch w=Stopwatch.StartNew(); while(!p.WaitForExit(200)) {
   if(stop.WaitOne(0)) {StopOwned(p); throw new OperationCanceledException("已停止当前任务。");}
   if(w.Elapsed>=timeout) {StopOwned(p);throw new TimeoutException("任务超时，已停止本次启动的助手进程树。");}
  }
  return p.ExitCode;
 }
 static string Tail(string file,long offset) {
  if(!File.Exists(file))return "";
  using(var f=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) {
   f.Seek(Math.Min(offset,f.Length),SeekOrigin.Begin); using(var r=new StreamReader(f,Encoding.UTF8))return r.ReadToEnd();
  }
 }
 public static void RunGame(Settings s,string game,EventWaitHandle stop) {
  if(stop.WaitOne(0))throw new OperationCanceledException();
  if(!Unlocked())throw new Exception("桌面已锁定或不可交互，本次任务跳过。请保持 Windows 登录并解锁。");
  if(new[]{"BetterGI","March7th Launcher","March7th Assistant","YuanShen","GenshinImpact","StarRail"}.Any(n=>Process.GetProcessesByName(n).Length>0))throw new Exception("已有游戏或助手运行，拒绝抢占。请先关闭它们再重试。");
  if(game=="genshin")CheckDragon(s);
  string name=game=="genshin"?"原神":"崩铁";
  string file=game=="genshin"?s.BetterGI:RailExe(s);
  string args=game=="genshin"?"startOneDragon "+Quote(s.Dragon):s.RailTask;
  var offsets=new Dictionary<string,long>(); string bgiLogs=Path.Combine(Path.GetDirectoryName(s.BetterGI),"log");
  if(game=="genshin"&&Directory.Exists(bgiLogs))foreach(string f in Directory.GetFiles(bgiLogs,"*.log")) offsets[f]=new FileInfo(f).Length;
  var info=new ProcessStartInfo(file,args){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(file),CreateNoWindow=game!="genshin"};
  if(game=="rail") { info.RedirectStandardOutput=true;info.RedirectStandardError=true;info.RedirectStandardInput=true;info.StandardOutputEncoding=Encoding.UTF8;info.StandardErrorEncoding=Encoding.UTF8;info.EnvironmentVariables["MARCH7TH_GUI_STARTED"]="1";info.EnvironmentVariables["PYTHONIOENCODING"]="utf-8"; }
  Log(name+"开始；命令："+file+" "+args);DateTime started=DateTime.Now; Process p=new Process();p.StartInfo=info;
  if(game=="rail") {p.OutputDataReceived+=(o,e)=>{if(e.Data!=null)Log("[崩铁] "+e.Data);};p.ErrorDataReceived+=(o,e)=>{if(e.Data!=null)Log("[崩铁] "+e.Data);};}
  p.Start();
  try {
   if(game=="rail"){p.StandardInput.Close();p.BeginOutputReadLine();p.BeginErrorReadLine();}
   int result=WaitChild(p,stop,TimeSpan.FromMinutes(s.TimeoutMinutes)); p.WaitForExit();
   if(result!=0)throw new Exception(name+"助手异常退出，退出码 "+result+"。请查看助手日志。");
   if(game=="genshin") {
    string text=Directory.Exists(bgiLogs)?string.Join("\n",Directory.GetFiles(bgiLogs,"*.log").Select(f=>Tail(f,offsets.ContainsKey(f)?offsets[f]:0)).ToArray()):"";
    if(!text.Contains("一条龙和配置组任务结束"))throw new Exception("原神助手已退出，但没有检测到一条龙结束记录，无法确认完成。");
    if(text.Contains("[ERR]")||text.Contains("执行配置组任务时失败"))throw new Exception("原神流程结束，但助手记录了错误，请检查 BetterGI 日志。");
    Log("原神：检测到一条龙结束记录，助手已退出；具体奖励请以游戏及助手日志为准。");
   } else {
    Log("崩铁：助手正常退出；具体任务完成情况请查看上方助手输出。");
    // The preflight rejected existing clients. Close only the newly launched StarRail client.
    foreach(var gameProcess in Process.GetProcessesByName("StarRail"))using(gameProcess){
     if(gameProcess.StartTime<started.AddSeconds(-2))throw new Exception("检测到无法归属本次任务的崩铁进程，请手动关闭。");
     gameProcess.CloseMainWindow();if(!gameProcess.WaitForExit(5000)){gameProcess.Kill();gameProcess.WaitForExit(5000);}if(!gameProcess.HasExited)throw new Exception("崩铁游戏未能关闭，停止后续任务。");
    }Log("本次新启动的崩铁游戏已关闭，释放桌面供下一任务使用。");
   }
  } finally {try{if(!p.HasExited)StopOwned(p);}finally{p.Dispose();}}
 }
 public static void StartBlackScreen(Settings s,EventWaitHandle stop) {
  if(!s.UseBlackScreen)return;
  if(Process.GetProcessesByName("BlackScreenMute").Length>0)throw new Exception("原版黑屏程序正在运行，请先按 Esc 退出；定时任务将使用不抢焦点的游戏兼容版。");
  string helper=Path.Combine(Root,"BlackScreenMute.Game.exe");
  foreach(var existing in Process.GetProcessesByName("BlackScreenMute.Game"))using(existing){if(string.Equals(existing.MainModule.FileName,helper,StringComparison.OrdinalIgnoreCase)){Log("复用已经开启的游戏黑屏静音。");return;}throw new Exception("另一位置的黑屏程序正在运行，请先按 Esc 退出。");}
  string ready=Path.Combine(Root,"blackout-ready-"+Guid.NewGuid().ToString("N")+".txt");
  using(var p=Process.Start(new ProcessStartInfo(helper,"--game-mode "+Quote(ready)){UseShellExecute=false,WorkingDirectory=Root})) {
   try {
    Stopwatch w=Stopwatch.StartNew();while(w.Elapsed<TimeSpan.FromSeconds(15)) {
     if(stop.WaitOne(0))throw new OperationCanceledException("启动黑屏时任务被停止，请按 Esc 恢复画面。");
     if(File.Exists(ready)){string text=File.ReadAllText(ready);if(text=="READY"){Log("已启动游戏兼容版黑屏静音；手动 Esc 恢复画面和声音，任务结束后保持黑屏。");return;}if(text.StartsWith("FAIL"))throw new Exception("黑屏静音启动失败："+text);}
     if(p.HasExited)throw new Exception("黑屏静音程序提前退出，未启动游戏。");Thread.Sleep(100);
    }throw new Exception("等待黑屏静音启动超时，未启动游戏。请按 Esc 检查画面。");
   }finally {if(File.Exists(ready))File.Delete(ready);}
  }
 }
 public static int Run(string target,bool scheduled) {
  bool held=false;using(var mutex=new Mutex(false,RunMutex)) using(var stop=new EventWaitHandle(false,EventResetMode.ManualReset,StopEvent)) {
   try {
    Log("收到"+(scheduled?"定时":"手动")+"任务："+target+"；等待桌面执行权。");
    try{held=mutex.WaitOne(0);}catch(AbandonedMutexException){held=true;}
    if(held)stop.Reset();
    else {DateTime end=DateTime.Now.AddHours(12);while(!held){int index;try{index=WaitHandle.WaitAny(new WaitHandle[]{stop,mutex},500);}catch(AbandonedMutexException){index=1;}if(index==0||DateTime.Now>end)throw new Exception("等待前一任务时超时或被停止。");held=index==1;}if(stop.WaitOne(0))throw new OperationCanceledException("排队任务已停止。");}
    Settings s=Load();Validate(s);
    SetThreadExecutionState(0x80000003);
    var order=target=="both"?(s.RailFirst?new[]{"rail","genshin"}:new[]{"genshin","rail"}):new[]{target};
    if(!Unlocked())throw new Exception("桌面已锁定，跳过任务。");
    if(new[]{"BetterGI","March7th Launcher","March7th Assistant","YuanShen","GenshinImpact","StarRail"}.Any(n=>Process.GetProcessesByName(n).Length>0))throw new Exception("已有游戏或助手运行，请先关闭后再运行。");
    if(order.Contains("genshin"))CheckDragon(s);
    StartBlackScreen(s,stop);
    foreach(string g in order) {if(g!="genshin"&&g!="rail")throw new Exception("任务参数无效。");RunGame(s,g,stop);}
    Log("本次计划执行结束。");return 0;
   } catch(Exception e){Log("本次计划终止："+e.Message);return 1;}
   finally{SetThreadExecutionState(0x80000000);if(held)mutex.ReleaseMutex();}
  }
 }
 public static List<KeyValuePair<string,string>> Plans(Settings s) {
  var p=new List<KeyValuePair<string,string>>();
  if(s.GenshinEnabled&&s.RailEnabled&&s.GenshinTime==s.RailTime) p.Add(new KeyValuePair<string,string>("both",s.GenshinTime));
  else {if(s.GenshinEnabled)p.Add(new KeyValuePair<string,string>("genshin",s.GenshinTime));if(s.RailEnabled)p.Add(new KeyValuePair<string,string>("rail",s.RailTime));}
  return p;
 }
 public static string TaskXml(Settings s,string target,string time) {
  string start=(s.Mode=="仅一次"?s.OnceDate:DateTime.Today.ToString("yyyy-MM-dd"))+"T"+time+":00";
  var b=new StringBuilder(); using(var x=XmlWriter.Create(b,new XmlWriterSettings{OmitXmlDeclaration=true,Indent=true})) {
   x.WriteStartElement("Task","http://schemas.microsoft.com/windows/2004/02/mit/task");x.WriteAttributeString("version","1.3");
   x.WriteStartElement("RegistrationInfo");x.WriteElementString("Description","原神与崩铁定时助手；仅交互式登录，关闭补跑，串行执行。");x.WriteEndElement();
   x.WriteStartElement("Triggers");x.WriteStartElement(s.Mode=="仅一次"?"TimeTrigger":"CalendarTrigger");x.WriteElementString("StartBoundary",start);x.WriteElementString("Enabled","true");
   if(s.Mode=="每天"){x.WriteStartElement("ScheduleByDay");x.WriteElementString("DaysInterval","1");x.WriteEndElement();}
   if(s.Mode=="每周"){x.WriteStartElement("ScheduleByWeek");x.WriteElementString("WeeksInterval","1");x.WriteStartElement("DaysOfWeek");string[] days={"Sunday","Monday","Tuesday","Wednesday","Thursday","Friday","Saturday"};for(int i=0;i<7;i++)if((s.Days&(1<<i))!=0)x.WriteElementString(days[i],"");x.WriteEndElement();x.WriteEndElement();}
   x.WriteEndElement();x.WriteEndElement();
   x.WriteStartElement("Principals");x.WriteStartElement("Principal");x.WriteAttributeString("id","Author");x.WriteElementString("UserId",WindowsIdentity.GetCurrent().User.Value);x.WriteElementString("LogonType","InteractiveToken");x.WriteElementString("RunLevel","HighestAvailable");x.WriteEndElement();x.WriteEndElement();
   x.WriteStartElement("Settings");x.WriteElementString("MultipleInstancesPolicy","IgnoreNew");x.WriteElementString("DisallowStartIfOnBatteries","false");x.WriteElementString("StopIfGoingOnBatteries","false");x.WriteElementString("StartWhenAvailable","false");x.WriteElementString("AllowStartOnDemand","true");x.WriteElementString("Enabled","true");x.WriteElementString("WakeToRun",s.Wake?"true":"false");x.WriteElementString("ExecutionTimeLimit","PT48H");x.WriteEndElement();
   x.WriteStartElement("Actions");x.WriteAttributeString("Context","Author");x.WriteStartElement("Exec");x.WriteElementString("Command",Exe);x.WriteElementString("Arguments","--run "+target+" --scheduled");x.WriteElementString("WorkingDirectory",Root);x.WriteEndElement();x.WriteEndElement();x.WriteEndElement();
  }return b.ToString();
 }
 public static dynamic Folder() {dynamic service=Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));service.Connect();return service.GetFolder("\\");}
 public static void Install(bool remove) {
  Settings s=Load();if(!remove){Validate(s);if(s.GenshinEnabled)CheckDragon(s);if(s.Mode=="仅一次")foreach(var p in Plans(s))if(DateTime.Parse(s.OnceDate+" "+p.Value)<=DateTime.Now)throw new Exception("单次执行时间必须晚于现在。");}
  dynamic folder=Folder();
  // Prevalidate all definitions before replacing any live task.
  var plans=remove?new List<KeyValuePair<string,string>>():Plans(s);
  foreach(var p in plans){dynamic service=Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));service.Connect();dynamic t=service.NewTask(0);t.XmlText=TaskXml(s,p.Key,p.Value);}
  var backup=new Dictionary<string,string>();foreach(string n in new[]{"genshin","rail","both"}){try{dynamic t=folder.GetTask(Id+"_"+n);backup[n]=Convert.ToString(t.Xml);}catch(Exception e){if((uint)e.HResult!=0x80070002)throw;}}
  try {
   foreach(var p in plans) folder.RegisterTask(Id+"_"+p.Key,TaskXml(s,p.Key,p.Value),6,null,null,3,null);
   foreach(string n in new[]{"genshin","rail","both"})if(!plans.Any(p=>p.Key==n)){try{folder.DeleteTask(Id+"_"+n,0);}catch(Exception e){if((uint)e.HResult!=0x80070002)throw;}}
  }catch{foreach(string n in new[]{"genshin","rail","both"}){try{if(backup.ContainsKey(n))folder.RegisterTask(Id+"_"+n,backup[n],6,null,null,3,null);else folder.DeleteTask(Id+"_"+n,0);}catch(Exception e){Log("恢复旧计划失败："+e.Message);}}throw;}
  Log(remove?"已移除本程序的所有定时计划。":"已应用定时计划："+string.Join("；",plans.Select(p=>p.Key+" "+p.Value).ToArray()));
 }
 public static string ScheduleStatus() {
  try {dynamic f=Folder();var rows=new List<string>();foreach(string n in new[]{"genshin","rail","both"}) {try {dynamic t=f.GetTask(Id+"_"+n);string label=n=="genshin"?"原神":n=="rail"?"崩铁":"两款顺序运行";rows.Add(label+"：下次 "+Convert.ToString(t.NextRunTime)+"，上次结果 "+Convert.ToString(t.LastTaskResult));}catch(Exception e){if((uint)e.HResult!=0x80070002)throw;}}return rows.Count==0?"尚未启用定时计划。":string.Join("\r\n",rows.ToArray());}catch(Exception e){return "读取计划状态失败："+e.Message;}
 }
 [STAThread] public static int Main(string[] args) {
  try {
   if(args.Length>0&&args[0]=="--mock-child"){Thread.Sleep(int.Parse(args[1]));return args.Length>2?int.Parse(args[2]):0;}
   if(args.Length>0&&args[0]=="--self-test")return Tests.Run();
   if(args.Length>0&&new[]{"--install","--remove","--stop","--run"}.Contains(args[0])) {
    if(!Admin){Elevated(string.Join(" ",args.Select(Quote).ToArray()),true);return 0;}
    if(args[0]=="--run")return Run(args[1],args.Contains("--scheduled"));
    if(args[0]=="--stop"){using(var ev=new EventWaitHandle(false,EventResetMode.ManualReset,StopEvent))ev.Set();Log("已发出停止信号（正在运行及排队的任务）。");}
    else Install(args[0]=="--remove");return 0;
   }
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new MainForm());return 0;
  }catch(Exception e){Log("错误："+e.Message);if(!args.Contains("--scheduled"))MessageBox.Show(e.Message,"定时助手",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
 }
}

static class AssistantFinder {
 public static bool Valid(string path,bool betterGI) {
  if(string.IsNullOrWhiteSpace(path)||!File.Exists(path))return false;
  if(!string.Equals(Path.GetFileName(path),betterGI?"BetterGI.exe":"March7th Launcher.exe",StringComparison.OrdinalIgnoreCase))return false;
  return betterGI||File.Exists(Path.Combine(Path.GetDirectoryName(path),"March7th Assistant.exe"));
 }
 public static List<string> Find(bool betterGI,string current) {
  var found=new List<string>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var watch=Stopwatch.StartNew();
  Action<string> add=p=>{try{if(Valid(p,betterGI)){p=Path.GetFullPath(p);if(seen.Add(p))found.Add(p);}}catch{}};
  string file=betterGI?"BetterGI.exe":"March7th Launcher.exe";
  // Shortcuts are authoritative even when an app was extracted into a custom folder.
  dynamic shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
  try {
   foreach(var root in new[]{Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)})
    Walk(root,4,watch,dir=>{foreach(string link in Files(dir,"*.lnk")){try{dynamic shortcut=shell.CreateShortcut(link);add(Convert.ToString(shortcut.TargetPath));Marshal.FinalReleaseComObject(shortcut);}catch{}}});
  }finally{Marshal.FinalReleaseComObject(shell);}
  add(current);
  foreach(var p in Process.GetProcessesByName(betterGI?"BetterGI":"March7th Launcher"))using(p){try{add(p.MainModule.FileName);}catch{}}
  foreach(var hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine})foreach(var view in new[]{RegistryView.Registry32,RegistryView.Registry64}) {
   try {using(var key=RegistryKey.OpenBaseKey(hive,view)) {
    using(var app=key.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\"+file)){if(app!=null)add(Convert.ToString(app.GetValue(null)));}
    using(var uninstall=key.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))if(uninstall!=null)foreach(string name in uninstall.GetSubKeyNames())using(var entry=uninstall.OpenSubKey(name)){
     if(entry==null)continue;string display=Convert.ToString(entry.GetValue("DisplayName"));
     if(display.IndexOf(betterGI?"BetterGI":"March7th",StringComparison.OrdinalIgnoreCase)<0&&display.IndexOf(betterGI?"更好的原神":"三月七",StringComparison.OrdinalIgnoreCase)<0)continue;
     string loc=Convert.ToString(entry.GetValue("InstallLocation"));if(!string.IsNullOrEmpty(loc))add(Path.Combine(loc,file));
     string icon=Convert.ToString(entry.GetValue("DisplayIcon")).Trim('"');int comma=icon.LastIndexOf(',');if(comma>0)icon=icon.Substring(0,comma).Trim('"');add(icon);
    }
   }}catch{}
  }
  if(found.Count>0)return found;
  var roots=new List<string>{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Documents")};
  foreach(var drive in DriveInfo.GetDrives()){try{if(drive.DriveType==DriveType.Fixed&&drive.IsReady)roots.Add(drive.RootDirectory.FullName);}catch{}}
  foreach(string root in roots.Distinct(StringComparer.OrdinalIgnoreCase)) {Walk(root,4,watch,dir=>add(Path.Combine(dir,file)));if(watch.Elapsed.TotalSeconds>=12)break;}
  return found;
 }
 static IEnumerable<string> Files(string dir,string pattern){try{return Directory.GetFiles(dir,pattern);}catch{return new string[0];}}
 static void Walk(string root,int depth,Stopwatch watch,Action<string> inspect) {
  if(string.IsNullOrWhiteSpace(root)||!Directory.Exists(root))return;
  var queue=new Queue<KeyValuePair<string,int>>();queue.Enqueue(new KeyValuePair<string,int>(root,0));
  while(queue.Count>0&&watch.Elapsed.TotalSeconds<12){var next=queue.Dequeue();try{
   if((File.GetAttributes(next.Key)&FileAttributes.ReparsePoint)!=0)continue;inspect(next.Key);if(next.Value>=depth)continue;
   foreach(string dir in Directory.GetDirectories(next.Key)){string n=Path.GetFileName(dir);if(new[]{"Windows","WindowsApps","$Recycle.Bin","System Volume Information",".git","node_modules","WebView2Data","libraries","3rdparty","AppData"}.Contains(n,StringComparer.OrdinalIgnoreCase))continue;queue.Enqueue(new KeyValuePair<string,int>(dir,next.Value+1));}
  }catch{}}
 }
}

class MainForm:Form {
 Settings s;TextBox bgi=new TextBox(),m7=new TextBox(),report=new TextBox(),logs=new TextBox();ComboBox dragon=new ComboBox(),task=new ComboBox(),mode=new ComboBox(),order=new ComboBox();CheckBox gen=new CheckBox(),rail=new CheckBox(),wake=new CheckBox(),blackout=new CheckBox();DateTimePicker gt=new DateTimePicker(),rt=new DateTimePicker(),date=new DateTimePicker();NumericUpDown timeout=new NumericUpDown();CheckedListBox days=new CheckedListBox();Label status=new Label();System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
 public MainForm() {
  s=App.Load();Text="原神 · 崩铁定时助手";Size=new Size(940,800);MinimumSize=new Size(940,800);StartPosition=FormStartPosition.CenterScreen;Font=new Font("Microsoft YaHei UI",10);BackColor=Color.FromArgb(246,248,252);AutoScaleMode=AutoScaleMode.Dpi;
  var tabs=new TabControl{Dock=DockStyle.Fill};Controls.Add(tabs);var page=new TabPage("计划设置");var record=new TabPage("运行记录");tabs.TabPages.Add(page);tabs.TabPages.Add(record);
  var panel=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=10,Padding=new Padding(18),AutoScroll=true};page.Controls.Add(panel);
  panel.Controls.Add(new Label{Text="设置时间，交给现有助手完成游戏任务",AutoSize=true,Font=new Font(Font,FontStyle.Bold),Margin=new Padding(0,0,0,12)});
  panel.Controls.Add(PathRow("BetterGI",bgi,s.BetterGI));panel.Controls.Add(PathRow("三月七启动器",m7,s.March7th));
  var g=Row();gen.Text="启用原神计划";gen.Checked=s.GenshinEnabled;gen.Width=145;g.Controls.Add(gen);g.Controls.Add(new Label{Text="时间",AutoSize=true,Margin=new Padding(4,8,4,0)});Time(gt,s.GenshinTime);g.Controls.Add(gt);g.Controls.Add(new Label{Text="一条龙配置",AutoSize=true,Margin=new Padding(15,8,4,0)});dragon.Width=210;dragon.DropDownStyle=ComboBoxStyle.DropDownList;g.Controls.Add(dragon);panel.Controls.Add(g);RefreshDragon();
  var r=Row();rail.Text="启用崩铁计划";rail.Checked=s.RailEnabled;rail.Width=145;r.Controls.Add(rail);r.Controls.Add(new Label{Text="时间",AutoSize=true,Margin=new Padding(4,8,4,0)});Time(rt,s.RailTime);r.Controls.Add(rt);r.Controls.Add(new Label{Text="任务内容",AutoSize=true,Margin=new Padding(15,8,4,0)});task.Width=210;task.DropDownStyle=ComboBoxStyle.DropDownList;
  task.Items.AddRange(new object[]{"完整运行 | main","日常 | routine","每日实训 | daily","清体力 | power","模拟宇宙 | universe","货币战争 | currencywars","差分宇宙 | divergent"});task.SelectedIndex=Math.Max(0,Array.IndexOf(new[]{"main","routine","daily","power","universe","currencywars","divergent"},s.RailTask));r.Controls.Add(task);panel.Controls.Add(r);
  var plan=Row();plan.Controls.Add(new Label{Text="执行周期",AutoSize=true,Margin=new Padding(0,8,10,0)});mode.DropDownStyle=ComboBoxStyle.DropDownList;mode.Items.AddRange(new object[]{"每天","每周","仅一次"});mode.SelectedItem=s.Mode;plan.Controls.Add(mode);date.Format=DateTimePickerFormat.Custom;date.CustomFormat="yyyy-MM-dd";date.Value=DateTime.Parse(s.OnceDate);date.Width=145;plan.Controls.Add(date);wake.Text="尝试唤醒睡眠中的电脑";wake.Checked=s.Wake;wake.Width=215;plan.Controls.Add(wake);blackout.Text="开始时黑屏静音";blackout.Checked=s.UseBlackScreen;blackout.Width=175;plan.Controls.Add(blackout);panel.Controls.Add(plan);
  var week=Row();days.Items.AddRange(new object[]{"周日","周一","周二","周三","周四","周五","周六"});days.MultiColumn=true;days.ColumnWidth=95;days.Size=new Size(700,40);days.CheckOnClick=true;for(int i=0;i<7;i++)days.SetItemChecked(i,(s.Days&(1<<i))!=0);week.Controls.Add(days);panel.Controls.Add(week);
  var opts=Row();opts.Controls.Add(new Label{Text="每个游戏最多运行（分钟）",AutoSize=true,Margin=new Padding(0,8,5,0)});timeout.Minimum=1;timeout.Maximum=720;timeout.Value=s.TimeoutMinutes;timeout.Width=80;opts.Controls.Add(timeout);opts.Controls.Add(new Label{Text="同时间 / 立即运行顺序",AutoSize=true,Margin=new Padding(20,8,5,0)});order.DropDownStyle=ComboBoxStyle.DropDownList;order.Items.AddRange(new object[]{"先原神，后崩铁","先崩铁，后原神"});order.SelectedIndex=s.RailFirst?1:0;order.Width=170;opts.Controls.Add(order);panel.Controls.Add(opts);
  var buttons=Row();buttons.Controls.Add(Button("保存设置",()=>{Save();MessageBox.Show("设置已保存。要让 Windows 按新时间执行，请点击“应用定时计划”。");}));buttons.Controls.Add(Button("应用定时计划",()=>{Save();App.Elevated("--install",true);RefreshStatus();}));buttons.Controls.Add(Button("关闭所有定时",()=>{App.Elevated("--remove",true);RefreshStatus();}));buttons.Controls.Add(Button("检查配置",()=>{Save();report.Text=App.Precheck(s);RefreshStatus();}));panel.Controls.Add(buttons);
  var manual=Row();manual.Controls.Add(Button("立即运行原神",()=>Launch("genshin")));manual.Controls.Add(Button("立即运行崩铁",()=>Launch("rail")));manual.Controls.Add(Button("立即顺序运行两款",()=>Launch("both")));manual.Controls.Add(Button("停止当前 / 排队任务",()=>App.Elevated("--stop",false)));panel.Controls.Add(manual);
  var bottom=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,AutoSize=true};status.AutoSize=true;bottom.Controls.Add(status);report.Multiline=true;report.ReadOnly=true;report.ScrollBars=ScrollBars.Vertical;report.Dock=DockStyle.Fill;report.Height=160;report.MinimumSize=new Size(800,160);bottom.Controls.Add(report);bottom.Controls.Add(new Label{AutoSize=true,MaximumSize=new Size(830,0),Text="电脑需开机、登录并解锁；运行期间不要操作鼠标键盘。游戏日常、副本、队伍和资源消耗在各助手中设置。应用计划后可关闭本窗口。"});panel.Controls.Add(bottom);
  logs.Multiline=true;logs.ReadOnly=true;logs.ScrollBars=ScrollBars.Both;logs.WordWrap=false;logs.Dock=DockStyle.Fill;logs.Font=new Font("Consolas",10);record.Controls.Add(logs);var bar=Row();bar.Dock=DockStyle.Bottom;bar.Controls.Add(Button("打开日志文件夹",()=>{Directory.CreateDirectory(Path.Combine(App.Root,"logs"));Process.Start(Path.Combine(App.Root,"logs"));}));bar.Controls.Add(Button("打开 BetterGI 设置",()=>OpenHelper(bgi.Text)));bar.Controls.Add(Button("打开三月七设置",()=>OpenHelper(m7.Text)));record.Controls.Add(bar);
  mode.SelectedIndexChanged+=(a,b)=>Toggle();Toggle();try{report.Text=App.Precheck(s);}catch(Exception e){report.Text=e.Message;}RefreshStatus();timer.Interval=2000;timer.Tick+=(a,b)=>RefreshLogs();timer.Start();RefreshLogs();
 }
 void OpenHelper(string path){Process.Start(new ProcessStartInfo(path){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(path)});}
 void Toggle(){date.Enabled=Convert.ToString(mode.SelectedItem)=="仅一次";days.Enabled=Convert.ToString(mode.SelectedItem)=="每周";}
 FlowLayoutPanel Row(){return new FlowLayoutPanel{AutoSize=true,WrapContents=false,Dock=DockStyle.Top,Margin=new Padding(0,3,0,5)};}
 Control PathRow(string title,TextBox box,string path){var row=Row();row.Controls.Add(new Label{Text=title,Width=140,Margin=new Padding(0,8,0,0)});box.Text=path;box.Width=470;row.Controls.Add(box);row.Controls.Add(Button("浏览",()=>{using(var d=new OpenFileDialog{Filter="程序或快捷方式|*.exe;*.lnk"})if(d.ShowDialog()==DialogResult.OK){string p=d.FileName;if(p.EndsWith(".lnk",StringComparison.OrdinalIgnoreCase)){dynamic shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));p=(string)shell.CreateShortcut(p).TargetPath;}box.Text=p;RefreshDragon();}}));var find=new Button{Text="自动查找",Name=box==bgi?"FindBetterGI":"FindMarch7th",AutoSize=true,Height=34,Padding=new Padding(6,2,6,2)};find.Click+=async(o,e)=>await FindAssistant(box,find,box==bgi);row.Controls.Add(find);return row;}
 async Task FindAssistant(TextBox box,Button button,bool betterGI) {
  string current=box.Text;button.Enabled=false;button.Text="查找中…";status.Text="正在查找"+(betterGI?" BetterGI":"三月七启动器")+"，请稍候…";
  try {
   var matches=await Task.Run(()=>AssistantFinder.Find(betterGI,current));if(IsDisposed)return;
   if(matches.Count==0){status.Text="未找到可用程序；请点“浏览”手动选择。";MessageBox.Show("在快捷方式、正在运行的程序、安装记录及常见目录中未找到可用程序。\r\n请点“浏览”选择程序或快捷方式。", "自动查找");return;}
   string selected=matches[0];
   if(matches.Count>1)using(var picker=new Form{Text="找到多个位置，请选择要使用的程序",Size=new Size(850,300),StartPosition=FormStartPosition.CenterParent,MinimizeBox=false,MaximizeBox=false,Font=Font}){
    var list=new ListBox{Dock=DockStyle.Fill,HorizontalScrollbar=true};list.Items.AddRange(matches.Cast<object>().ToArray());list.SelectedIndex=0;picker.Controls.Add(list);var ok=new Button{Text="使用选中的位置",Dock=DockStyle.Bottom,Height=40,DialogResult=DialogResult.OK};picker.Controls.Add(ok);picker.AcceptButton=ok;list.DoubleClick+=(o,e)=>{picker.DialogResult=DialogResult.OK;picker.Close();};if(picker.ShowDialog(this)!=DialogResult.OK){status.Text="已取消选择，原路径保持不变。";return;}selected=list.SelectedItem.ToString();
   }
   box.Text=selected;if(betterGI)RefreshDragon();status.Text="已找到"+(betterGI?" BetterGI":"三月七启动器")+"；请保存设置。";
  }catch(Exception ex){if(!IsDisposed){status.Text="自动查找未完成。";MessageBox.Show(ex.Message,"自动查找");}}
  finally{if(!IsDisposed){button.Enabled=true;button.Text="自动查找";}}
 }
 Button Button(string text,Action action){var b=new Button{Text=text,AutoSize=true,Height=34,Padding=new Padding(6,2,6,2)};b.Click+=(o,e)=>{try{action();}catch(Exception ex){MessageBox.Show(ex.Message,"操作未完成",MessageBoxButtons.OK,MessageBoxIcon.Warning);}};return b;}
 void Time(DateTimePicker p,string value){p.Format=DateTimePickerFormat.Custom;p.CustomFormat="HH:mm";p.ShowUpDown=true;p.Width=95;p.Value=DateTime.Today.Add(TimeSpan.Parse(value));}
 void RefreshDragon(){string old=dragon.SelectedItem==null?s.Dragon:dragon.SelectedItem.ToString();dragon.Items.Clear();if(string.IsNullOrWhiteSpace(bgi.Text))return;string dir=Path.Combine(Path.GetDirectoryName(bgi.Text)??"",@"User\OneDragon");if(Directory.Exists(dir))foreach(string f in Directory.GetFiles(dir,"*.json")){try{var d=App.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(f));dragon.Items.Add(Convert.ToString(d["Name"]));}catch{}}if(dragon.Items.Contains(old))dragon.SelectedItem=old;else if(dragon.Items.Count>0)dragon.SelectedIndex=0;}
 void Save(){var n=new Settings{BetterGI=bgi.Text.Trim(),March7th=m7.Text.Trim(),Dragon=Convert.ToString(dragon.SelectedItem),RailTask=task.SelectedItem.ToString().Split('|')[1].Trim(),GenshinEnabled=gen.Checked,RailEnabled=rail.Checked,GenshinTime=gt.Value.ToString("HH:mm"),RailTime=rt.Value.ToString("HH:mm"),Mode=mode.SelectedItem.ToString(),OnceDate=date.Value.ToString("yyyy-MM-dd"),Days=0,TimeoutMinutes=(int)timeout.Value,RailFirst=order.SelectedIndex==1,Wake=wake.Checked,UseBlackScreen=blackout.Checked};foreach(int i in days.CheckedIndices)n.Days|=1<<i;App.Save(n);s=n;}
 void Launch(string target){Save();if(target!="rail")App.CheckDragon(s);App.Elevated("--run "+target,false);}
 void RefreshStatus(){status.Text=App.ScheduleStatus();}
 void RefreshLogs(){string p=Path.Combine(App.Root,"logs",DateTime.Now.ToString("yyyy-MM-dd")+".log");try{string text=File.Exists(p)?File.ReadAllText(p):"尚无运行记录。";if(text.Length>80000)text=text.Substring(text.Length-80000);if(logs.Text!=text){logs.Text=text;logs.SelectionStart=logs.Text.Length;logs.ScrollToCaret();}}catch(IOException){}}
}

static class Tests {
 static void Assert(bool ok,string name){if(!ok)throw new Exception("TEST FAILED: "+name);}
 public static int Run(){var results=new List<string>();try{
  Settings s=App.Load();App.Validate(s);App.CheckDragon(s);results.Add("本机路径和原神配置检查通过");
  Assert(!AssistantFinder.Valid(s.March7th,true)&&!AssistantFinder.Valid(s.BetterGI,false),"reject wrong helper");
  var bgMatches=AssistantFinder.Find(true,"");var railMatches=AssistantFinder.Find(false,"");Assert(bgMatches.Any(p=>string.Equals(p,s.BetterGI,StringComparison.OrdinalIgnoreCase)),"discover BetterGI shortcut");Assert(railMatches.Any(p=>string.Equals(p,s.March7th,StringComparison.OrdinalIgnoreCase)),"discover March7th shortcut");results.Add("自动查找实际找到了两款助手；程序类型及崩铁完整目录校验通过");
  string name="中文配置 空格";Assert(App.Quote(name)=="\""+name+"\"","quote");
  s.GenshinEnabled=s.RailEnabled=true;s.RailTime=s.GenshinTime;Assert(App.Plans(s).Count==1&&App.Plans(s)[0].Key=="both","combine");s.RailTime="07:30";Assert(App.Plans(s).Count==2,"separate");
  foreach(string mode in new[]{"每天","每周","仅一次"}){s.Mode=mode;s.Days=42;string xml=App.TaskXml(s,"both","05:30");var d=new XmlDocument();d.LoadXml(xml);dynamic svc=Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));svc.Connect();dynamic t=svc.NewTask(0);t.XmlText=xml;Assert(xml.Contains("InteractiveToken")&&xml.Contains("<StartWhenAvailable>false</StartWhenAvailable>"),"interactive");if(mode=="每周")Assert(xml.Contains("Monday")&&xml.Contains("Wednesday")&&xml.Contains("Friday")&&!xml.Contains("<Sunday"),"weekdays");}results.Add("每天、每周、单次计划由 Windows 任务定义解析通过；未注册计划");
  using(var ev=new EventWaitHandle(false,EventResetMode.ManualReset)){
   Func<int,int,Process> child=(ms,exit)=>Process.Start(new ProcessStartInfo(App.Exe,"--mock-child "+ms+" "+exit){UseShellExecute=false,CreateNoWindow=true});
   using(var p=child(50,0))Assert(App.WaitChild(p,ev,TimeSpan.FromSeconds(10))==0,"success");
   using(var p=child(50,7))Assert(App.WaitChild(p,ev,TimeSpan.FromSeconds(10))==7,"failed exit");
   using(var p=child(10000,0)){bool hit=false;try{App.WaitChild(p,ev,TimeSpan.FromMilliseconds(500));}catch(TimeoutException){hit=true;}Assert(hit&&p.HasExited,"timeout kill");}
   ev.Set();using(var p=child(10000,0)){bool hit=false;try{App.WaitChild(p,ev,TimeSpan.FromSeconds(10));}catch(OperationCanceledException){hit=true;}Assert(hit&&p.HasExited,"cancel kill");}
  }results.Add("模拟助手：正常退出、错误退出、超时终止、停止终止通过");
  using(var m=new Mutex(false,"Local\\"+App.Id+"_Test")){Assert(m.WaitOne(0),"lock");bool blocked=Task.Run(()=>{using(var n=new Mutex(false,"Local\\"+App.Id+"_Test")){bool got=n.WaitOne(100);if(got)n.ReleaseMutex();return !got;}}).Result;Assert(blocked,"serialize");m.ReleaseMutex();}results.Add("跨线程互斥，避免两款游戏同时运行通过");
  Application.EnableVisualStyles();using(var f=new MainForm()){f.Show();Application.DoEvents();Assert(Descendants(f).OfType<Button>().Count(b=>b.Text=="自动查找")==2,"find buttons");var combos=Descendants(f).OfType<ComboBox>().ToArray();foreach(var c in combos){Assert(c.SelectedIndex>=0,"UI combo selection");results.Add("界面默认选择："+Convert.ToString(c.SelectedItem));}using(var b=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(b,new Rectangle(0,0,b.Width,b.Height));b.Save(Path.Combine(App.Root,"界面预览.png"));}f.Close();}results.Add("设置界面已实际创建并生成预览");
  File.WriteAllLines(Path.Combine(App.Root,"验证结果.txt"),results.Concat(new[]{"没有启动真实游戏，没有注册或启用定时任务。"}),Encoding.UTF8);return 0;
 }catch(Exception e){File.WriteAllText(Path.Combine(App.Root,"验证结果.txt"),string.Join("\r\n",results.ToArray())+"\r\n"+e.ToString(),Encoding.UTF8);return 1;}}
 static IEnumerable<Control> Descendants(Control c){foreach(Control child in c.Controls){yield return child;foreach(Control d in Descendants(child))yield return d;}}
}
