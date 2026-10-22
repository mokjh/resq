// eval_file script: list outstanding Meta Project Setup Tool tasks for Android.
var group = UnityEditor.BuildTargetGroup.Android;
var getTasks = typeof(OVRProjectSetup).GetMethod("GetTasks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
var tasks = (System.Collections.IEnumerable)getTasks.Invoke(null, new object[] { group });
var sb = new System.Text.StringBuilder();
int open = 0;
foreach (var t in tasks)
{
    var tt = t.GetType();
    var isDone = (System.Func<UnityEditor.BuildTargetGroup, bool>)tt.GetProperty("IsDone").GetValue(t);
    if (isDone(group)) continue;
    open++;
    var msg = tt.GetProperty("Message").GetValue(t);
    var lvl = tt.GetProperty("Level").GetValue(t);
    string m = msg.GetType().GetMethod("GetValue", new[] { typeof(UnityEditor.BuildTargetGroup) })?.Invoke(msg, new object[] { group })?.ToString();
    string l = lvl.GetType().GetMethod("GetValue", new[] { typeof(UnityEditor.BuildTargetGroup) })?.Invoke(lvl, new object[] { group })?.ToString();
    sb.AppendLine($"[{l}] {m}");
}
return $"open={open}\n" + sb.ToString();
