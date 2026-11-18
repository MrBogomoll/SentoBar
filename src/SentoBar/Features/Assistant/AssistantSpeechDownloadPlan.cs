using System.Collections.Generic;
using System.IO;

namespace SentoBar;

internal enum SpeechDownloadState { Installed, Cached, Required }

internal sealed record SpeechDownloadItem(string Name,string FileName,long Bytes,SpeechDownloadState State)
{
    internal long UpperBytes=>Bytes>0?Bytes:128*1024;
}

internal sealed partial class AssistantSpeechInstall
{
    internal IReadOnlyList<SpeechDownloadItem> DownloadPlan(SpeechAsrModel asr,SpeechTtsModel tts,bool gpu)
    {
        var items=new List<SpeechDownloadItem>();
        void Add(string name,string path,long bytes,bool installed)
        {
            var state=installed?SpeechDownloadState.Installed:HasFile(path,bytes)?SpeechDownloadState.Cached:SpeechDownloadState.Required;
            items.Add(new(name,Path.GetFileName(path),bytes,state));
        }
        void Package(string name,SpeechAsset asset,string folder,string marker)
            =>Add(name,RuntimeArchive(asset,folder),asset.Bytes,Ready(folder,marker));
        void Weight(string name,string path,long bytes)=>Add(name,path,bytes,HasFile(path,bytes));

        if(!CppRuntimePresent())
            Add(Localization.Text("Microsoft Visual C++ runtime"),Path.Combine(root,"downloads","VC_redist.x64.exe"),SpeechCatalog.VisualCpp.Bytes,false);
        if(asr.Native)
        {
            var folder=Path.Combine(root,"models",asr.Id);
            if(asr.Files==null)Package(asr.Name,asr.Asset,folder,AsrMarker(asr));
            else
            {
                bool installed=Ready(folder,AsrMarker(asr));
                foreach(var file in asr.Files)
                    Add(asr.Name,Path.Combine(root,"downloads",asr.Id,file.Path),file.Asset.Bytes,installed);
            }
        }
        else
        {
            Weight(asr.Name,AsrPath(asr),asr.Asset.Bytes);
            Package(Localization.Text("Whisper CPU engine"),SpeechCatalog.Cpu,EngineFolder(false),WhisperExe(false));
            if(gpu)Package(Localization.Text("Whisper GPU engine"),SpeechCatalog.Gpu,EngineFolder(true),WhisperExe(true));
        }
        if(asr.Native || tts.Package!=null)
        {
            bool ready=(!asr.Native || Ready(SherpaRoot,SherpaExe(false))) && (tts.Package==null || Ready(SherpaRoot,SherpaExe(true)));
            Add(Localization.Text("Sherpa native speech engine"),RuntimeArchive(SpeechCatalog.Sherpa,SherpaRoot),SpeechCatalog.Sherpa.Bytes,ready);
        }
        if(tts.Package!=null)
            Package(tts.Name,tts.Package,VoiceRoot(tts),"");
        else if(tts.Id!="windows")
        {
            Package(Localization.Text("Piper speech engine"),SpeechCatalog.Piper,PiperFolder,PiperExe);
            string path=TtsPath(tts);
            Weight(tts.Name,path,tts.Bytes);
            Weight(Localization.Text("Voice configuration"),path+".json",0);
            Weight(Localization.Text("Voice attribution and model card"),path+".MODEL_CARD",0);
        }
        return items;
    }
}
