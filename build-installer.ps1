$ErrorActionPreference = "Stop"

$projectDir = $PSScriptRoot
$releaseDir = Join-Path $projectDir "bin\Release\net10.0-windows"
$outputSetup = Join-Path $projectDir "YouTubeAudioDownloader_Setup.exe"

Write-Host "1. Budowanie projektu (Release)..."
dotnet build "$projectDir\YouTubeAudioDownloader.csproj" -c Release --nologo -v quiet

Write-Host "2. Pakowanie plikow aplikacji..."
$tempZip = Join-Path $env:TEMP "yt_app_payload.zip"
if (Test-Path $tempZip) { Remove-Item $tempZip -Force }

$filesToPack = Get-ChildItem -Path $releaseDir -File | Where-Object { $_.Extension -ne ".pdb" } | Select-Object -ExpandProperty FullName
Compress-Archive -Path $filesToPack -DestinationPath $tempZip -CompressionLevel Optimal

$tempCs = Join-Path $env:TEMP "SetupStub.cs"
@'
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("YouTube Audio Downloader - Instalator")]
[assembly: AssemblyProduct("YouTube Audio Downloader")]
[assembly: AssemblyVersion("1.0.0.0")]

class SetupProgram
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        var ask = MessageBox.Show(
            "Czy chcesz zainstalować program YouTube Audio Downloader (MP3) oraz utworzyć skrót na Pulpicie?",
            "Instalator - YouTube Audio Downloader",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (ask != DialogResult.Yes)
            return;

        try
        {
            string targetDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                "YouTubeAudioDownloader");

            Directory.CreateDirectory(targetDir);

            string tempZip = Path.Combine(Path.GetTempPath(), "yt_setup_" + Guid.NewGuid().ToString("N") + ".zip");
            using (Stream res = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
            using (FileStream fs = File.Create(tempZip))
            {
                res.CopyTo(fs);
            }

            foreach (string existing in Directory.GetFiles(targetDir))
            {
                try { File.Delete(existing); } catch { }
            }

            ZipFile.ExtractToDirectory(tempZip, targetDir);
            try { File.Delete(tempZip); } catch { }

            string exePath = Path.Combine(targetDir, "YouTubeAudioDownloader.exe");
            CreateShortcut(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "YouTube Audio Downloader.lnk"),
                exePath,
                targetDir);

            CreateShortcut(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "YouTube Audio Downloader.lnk"),
                exePath,
                targetDir);

            var runNow = MessageBox.Show(
                "Instalacja zakończona pomyślnie!\nUtworzono skrót na Pulpicie oraz w Menu Start.\n\nCzy chcesz uruchomić program teraz?",
                "Instalacja zakończona",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (runNow == DialogResult.Yes)
            {
                Process.Start(new ProcessStartInfo(exePath) { WorkingDirectory = targetDir, UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Błąd podczas instalacji:\n" + ex.Message, "Błąd instalatora", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    static void CreateShortcut(string shortcutPath, string targetPath, string workingDir)
    {
        Type shellType = Type.GetTypeFromProgID("WScript.Shell");
        dynamic shell = Activator.CreateInstance(shellType);
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = targetPath;
        shortcut.WorkingDirectory = workingDir;
        shortcut.Description = "YouTube Audio Downloader (MP3)";
        shortcut.Save();
    }
}
'@ | Set-Content -Path $tempCs -Encoding UTF8

Write-Host "3. Kompilowanie pojedynczego pliku YouTubeAudioDownloader_Setup.exe..."
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /nologo /target:winexe /optimize+ /out:"$outputSetup" /r:System.IO.Compression.FileSystem.dll /r:System.Windows.Forms.dll /r:Microsoft.CSharp.dll /res:"$tempZip,payload.zip" "$tempCs"

Remove-Item $tempZip -Force -ErrorAction SilentlyContinue
Remove-Item $tempCs -Force -ErrorAction SilentlyContinue

if (Test-Path $outputSetup) {
    $sizeMb = [Math]::Round((Get-Item $outputSetup).Length / 1MB, 2)
    Write-Host "Gotowe! Utworzono: $outputSetup ($sizeMb MB)"
} else {
    throw "Nie udalo sie utworzyc instalatora."
}
