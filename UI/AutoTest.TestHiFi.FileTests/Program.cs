using AutoTest.TestHiFi;
using System.Text;

static void Ensure(bool value, string message)
{
    if (!value) throw new InvalidOperationException("UI-02 IO TEST FAIL: " + message);
}

var root = Path.Combine(Path.GetTempPath(), "AutoTestUi02Test_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var service = new UiWorkspaceFiles();
    service.Create(root,"Sample Project");
    Ensure(File.Exists(service.Manifest!), "manifest created");

    bool reservedRejected=false;
    try { service.NewAsset("测试程序","CON"); }
    catch (ArgumentException) { reservedRejected=true; }
    Ensure(reservedRejected,"Windows reserved resource names rejected");

    var test = service.NewAsset("测试程序","Test One");
    Ensure(File.Exists(service.FullPath(test.RelativePath)), "test asset written");
    var doc = service.OpenAsset(test);
    doc.Content = "{\"uiOnly\":true,\"notes\":\"中文内容 and edits\"}\n";
    Ensure(doc.Dirty,"dirty flag");
    service.Save(doc);
    Ensure(!doc.Dirty,"dirty reset after disk save");
    Ensure(File.ReadAllText(service.FullPath(test.RelativePath)).Contains("中文内容"),"UTF8 persisted");

    service.Rename(test,"Test Two");
    Ensure(File.Exists(service.FullPath(test.RelativePath)),"rename actual file");
    service.MoveAssetToKind(test,"其他配置");
    Ensure(test.RelativePath.StartsWith("config/"),"move asset category");
    var clone = service.Duplicate(test);
    Ensure(clone.Id!=test.Id&&File.Exists(service.FullPath(clone.RelativePath)),"new identity and copied disk content");

    service.Delete(clone);
    Ensure(service.Workspace!.Assets.All(x=>x.Id!=clone.Id),"deleted manifest record");
    Ensure(service.Workspace.DeletedAssets.Any(x=>x.Id==clone.Id),"recoverable trash record");
    var recovered=service.RestoreLastDeleted();
    Ensure(recovered?.Id==clone.Id,"recover same ID and filename");
    Ensure(File.Exists(service.FullPath(clone.RelativePath)),"recovered file content");

    var source = Path.Combine(root,"import.dbc");
    File.WriteAllText(source,"VERSION \"TEST\"\n",new UTF8Encoding(true));
    var dbc=service.Import(source,"DBC");
    var imported=service.OpenAsset(dbc);
    imported.Content+="CM_ \"中文\";\n";
    service.Save(imported);
    var encoded=File.ReadAllBytes(service.FullPath(dbc.RelativePath));
    Ensure(encoded.Length>3&&encoded[0]==0xEF&&encoded[1]==0xBB&&encoded[2]==0xBF,
           "UTF8 BOM preserved");

    var manifest=service.Manifest!;
    service.Close();
    Ensure(!service.HasProject,"closing project clears state");
    service.Open(manifest);
    Ensure(service.Workspace!.Assets.Count==3,"reopen all three assets");
    var second=service.Workspace.Assets.Single(x=>x.Name=="Test Two");
    Ensure(service.OpenAsset(second).Content.Contains("中文内容"),"reload from actual disk");

    // A legacy file with malformed UTF-8 must not be silently rewritten.
    var legacy=Path.Combine(root,"legacy.dbc");
    File.WriteAllBytes(legacy,[0x82,0xFF,0x80]);
    var old=service.Import(legacy,"DBC");
    bool rejected=false;
    try { service.OpenAsset(old); }
    catch(InvalidDataException) { rejected=true; }
    Ensure(rejected,"lossy legacy decoding refused");

    Console.WriteLine("UI-02 file lifecycle checks passed (new/open/write/save/rename/move/copy/trash/restore/BOM/reload/encoding guard).");
}
finally
{
    try { Directory.Delete(root,true); } catch(IOException) { }
}
