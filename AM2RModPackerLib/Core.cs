﻿using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Security;
using System.Security.Cryptography;
using AM2RModPackerLib.XML;

namespace AM2RModPackerLib;

public enum ProfileOperatingSystems
{
    Windows,
    Linux,
    Mac
}

public static class Core
{
    public static readonly string Version = "2.0.3";
    private const string originalMD5 = "f2b84fe5ba64cb64e284be1066ca08ee";
    private static readonly string[] DATAFILES_BLACKLIST = { "data.win", "AM2R.exe", "D3DX9_43.dll", "game.unx", "game.ios" };
    // todo: dont do this, cwd is unreliable
    private static readonly string localPath = Directory.GetCurrentDirectory();
    
    // TODO: go over thhis and clean
    public static (bool, string) CreateModPack(ModProfileXML profile, string originalZipPath, string modZipPath, string apkPath, string output)
    {
        // Cleanup in case of previous errors
        if (Directory.Exists(Path.GetTempPath() + "/AM2RModPacker"))
            Directory.Delete(Path.GetTempPath() + "/AM2RModPacker", true);

        // Create temp work folders
        string tempPath,
               tempOriginalPath,
               tempModPath,
               tempProfilePath;

        // We might not have permission to access to the temp directory, so we need to catch the exception.
        try
        {
            tempPath = Directory.CreateDirectory(Path.GetTempPath() + "/AM2RModPacker").FullName;
            tempOriginalPath = Directory.CreateDirectory(tempPath + "/original").FullName;
            tempModPath = Directory.CreateDirectory(tempPath + "/mod").FullName;
            tempProfilePath = Directory.CreateDirectory(tempPath + "/profile").FullName;
        }
        catch (SecurityException)
        {
            return (false, "Could not create temp directory! Please run the application with administrator rights.");
        }

        // Extract 1.1 and modded AM2R to their own directories in temp work
        ZipFile.ExtractToDirectory(originalZipPath, tempOriginalPath);
        ZipFile.ExtractToDirectory(modZipPath, tempModPath);

        if (Directory.Exists(tempModPath + "/AM2R"))
            tempModPath += "/AM2R";

        // Verify 1.1 with an MD5. If it does not match, exit cleanly and provide a warning window.
        try
        {
            // TODO: dont. do what launcher does
            string newMD5 = CalculateMD5(tempOriginalPath + "/data.win");

            if (newMD5 != originalMD5)
                return (false, "1.1 data.win does not meet MD5 checksum! Mod packaging aborted.\n1.1 MD5: " + originalMD5 + "\nYour MD5: " + newMD5);
        }
        catch (FileNotFoundException)
        {
            return (false, "data.win not found! Are you sure you selected AM2R 1.1? Mod packaging aborted.");
        }

        switch (profile.OperatingSystem)
        {
            // Create AM2R.exe and data.win patches
            case "Windows":
            {
                if (profile.UsesYYC)
                {
                    CreatePatch(tempOriginalPath + "/data.win", tempModPath + "/AM2R.exe", tempProfilePath + "/AM2R.xdelta");
                }
                else
                {
                    CreatePatch(tempOriginalPath + "/data.win", tempModPath + "/data.win", tempProfilePath + "/data.xdelta");
                    CreatePatch(tempOriginalPath + "/AM2R.exe", tempModPath + "/AM2R.exe", tempProfilePath + "/AM2R.xdelta");
                }
                break;
            }
            case "Linux":
            {
                string runnerName = File.Exists(tempModPath + "/" + "AM2R") ? "AM2R" : "runner";
                CreatePatch(tempOriginalPath + "/data.win", tempModPath + "/assets/game.unx", tempProfilePath + "/game.xdelta");
                CreatePatch(tempOriginalPath + "/AM2R.exe", tempModPath + "/" + runnerName, tempProfilePath + "/AM2R.xdelta");
                break;
            }
            case "Mac":
            {
                CreatePatch(tempOriginalPath + "/data.win", tempModPath + "/AM2R.app/Contents/Resources/game.ios", tempProfilePath + "/game.xdelta");
                CreatePatch(tempOriginalPath + "/AM2R.exe", tempModPath + "/AM2R.app/Contents/MacOS/Mac_Runner", tempProfilePath + "/AM2R.xdelta");

                // Copy plist over for custom title name
                File.Copy(tempModPath + "/AM2R.app/Contents/Info.plist", tempProfilePath + "/Info.plist");
                break;
            }
        }
        
        // Create game.droid patch and wrapper if Android is supported
        if (profile.Android)
        {
            string tempAndroid = Directory.CreateDirectory(tempPath + "/android").FullName;

            // Extract APK 
            // - java -jar apktool.jar d "%~dp0AM2RWrapper_old.apk"

            // Process startInfo
            // TODO: cross platform
            var procStartInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                WorkingDirectory = tempAndroid,
                Arguments = "/C java -jar \"" + localPath + "/utilities/android/apktool.jar\" d -f -o \"" + tempAndroid + "\" \"" + apkPath + "\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // Run process
            using (var proc = new Process { StartInfo = procStartInfo })
            {
                proc.Start();
                proc.WaitForExit();
            }

            // Create game.droid patch
            CreatePatch(tempOriginalPath + "/data.win", tempAndroid + "/assets/game.droid", tempProfilePath + "/droid.xdelta");

            // Delete excess files in APK

            // Create whitelist
            string[] whitelist = { "splash.png", "portrait_splash.png" };

            // Get directory
            var androidAssets = new DirectoryInfo(tempAndroid + "/assets");


            // Delete files
            foreach (var file in androidAssets.GetFiles())
            {
                if (file.Name.EndsWith(".ini") && file.Name != "modifiers.ini")
                {
                    if (File.Exists(tempProfilePath + "/AM2R.ini"))
                        // This shouldn't be a problem... normally...
                        File.Delete(tempProfilePath + "/AM2R.ini");
                    File.Copy(file.FullName, tempProfilePath + "/AM2R.ini");
                }

                if (!whitelist.Contains(file.Name))
                    File.Delete(file.FullName);
            }

            foreach (var dir in androidAssets.GetDirectories())
                Directory.Delete(dir.FullName, true);

            // Create wrapper

            // Process startInfo
            // - java -jar apktool.jar b "%~dp0AM2RWrapper_old" -o "%~dp0AM2RWrapper.apk"
            var procStartInfo2 = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                WorkingDirectory = tempAndroid,
                Arguments = "/C java -jar \"" + localPath + "/utilities/android/apktool.jar\" b -f \"" + tempAndroid + "\" -o \"" + tempProfilePath + "/AM2RWrapper.apk\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // Run process
            using (var proc = new Process { StartInfo = procStartInfo2 })
            {
                proc.Start();
                proc.WaitForExit();
            }

            string tempAndroidProfilePath = tempProfilePath + "/android";
            Directory.CreateDirectory(tempAndroidProfilePath);

            File.Move(tempProfilePath + "/AM2RWrapper.apk", tempAndroidProfilePath + "/AM2RWrapper.apk");
            if (File.Exists(tempProfilePath + "/AM2R.ini"))
                File.Move(tempProfilePath + "/AM2R.ini", tempAndroidProfilePath + "/AM2R.ini");
        }

        // Copy datafiles (exclude .ogg if custom music is not selected)

        var dirInfo = new DirectoryInfo(tempModPath);
        if (profile.OperatingSystem == "Linux")
            dirInfo = new DirectoryInfo(tempModPath + "/assets");
        else if (profile.OperatingSystem == "Mac")
            dirInfo = new DirectoryInfo(tempModPath + "/AM2R.app/Contents/Resources");

        Directory.CreateDirectory(tempProfilePath + "/files_to_copy");

        if (profile.UsesCustomMusic)
        {
            // Copy files, excluding the blacklist
            CopyFilesRecursive(dirInfo, DATAFILES_BLACKLIST, tempProfilePath + "/files_to_copy");
        }
        else
        {
            // Get list of 1.1's music files
            string[] musFiles = Directory.GetFiles(tempOriginalPath, "*.ogg").Select(file => Path.GetFileName(file)).ToArray();

            if (profile.OperatingSystem == "Linux" || profile.OperatingSystem == "Mac")
                musFiles = Directory.GetFiles(tempOriginalPath, "*.ogg").Select(file => Path.GetFileName(file).ToLower()).ToArray();


            // Combine musFiles with the known datafiles for a blacklist
            string[] blacklist = musFiles.Concat(DATAFILES_BLACKLIST).ToArray();

            // Copy files, excluding the blacklist
            CopyFilesRecursive(dirInfo, blacklist, tempProfilePath + "/files_to_copy");
        }

        // Export profile as XML
        string xmlOutput = Serializer.Serialize<ModProfileXML>(profile);
        File.WriteAllText(tempProfilePath + "/profile.xml", xmlOutput);

        // Compress temp folder to .zip
        if (File.Exists(output))
            File.Delete(output);

        ZipFile.CreateFromDirectory(tempProfilePath, output);

        // Delete temp folder
        Directory.Delete(tempPath, true);
        return (true, "");
    }

    
    public static void CreatePatch(string original, string modified, string output)
    {
        // Specify process start info
        var parameters = new ProcessStartInfo
        {
            FileName = OS.IsWindows ? localPath + "/utilities/xdelta/xdelta3.exe" : "xdelta3",
            WorkingDirectory = localPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            Arguments = "-f -e -s \"" + original + "\" \"" + modified + "\" \"" + output + "\""
        };

        // Launch process and wait for exit.
        try
        {
            using var proc = new Process { StartInfo = parameters };
            proc.Start();
            proc.WaitForExit();
        }
        catch (Win32Exception e)
        {
            throw new Exception("Xdelta3 could not be found! For Windows, make sure that the utilities folder exists, for other OS make sure it is installed and in PATH.");
        }
    }
    
    public static string CalculateMD5(string filename)
    {
        using var stream = File.OpenRead(filename);
        using var md5 = MD5.Create();
        byte[] hash = md5.ComputeHash(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }
    
    public static void CopyFilesRecursive(DirectoryInfo source, string[] blacklist, string destination)
    {
        foreach (var file in source.GetFiles())
        {
            if (!blacklist.Contains(file.Name))
                file.CopyTo(destination + "/" + file.Name);
        }

        foreach (var dir in source.GetDirectories())
        {
            // Folders need to be lowercase, because GM only reads from lowercase names on *nix systems. Windows is case-insensitive so doesnt matter for them
            string newDir = Directory.CreateDirectory(destination + "/" + dir.Name.ToLower()).FullName;
            CopyFilesRecursive(dir, blacklist, newDir);
        }
    }
}