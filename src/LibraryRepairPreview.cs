using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace PlaylistFlac
{
    internal static class LibraryRepairPreview
    {
        internal static string Describe(string folder,Playlist playlist,CancellationToken ct)
        {
            var index=IndexStore.Read(LibraryLayout.PathFor(folder,"_index.csv"));
            var duplicates=new List<string>();var renames=new List<string>();
            var cleanup=DuplicateLibrary.Consolidate(folder,playlist,RecordingGroups.Build(playlist.Tracks),index,ct,null,duplicates);
            LibraryNaming.RenameCompleted(folder,playlist,ct,null,"playlist",renames);
            var text=new StringBuilder("Preview only — no files have been changed.\r\n\r\n");
            text.AppendLine("Duplicate files to archive: "+cleanup.ArchivedFiles+"; playlist references to reuse: "+cleanup.LinkedEntries);
            foreach(var item in duplicates)text.AppendLine("  "+item);
            text.AppendLine();text.AppendLine("Proposed filenames before duplicate consolidation: "+renames.Count);
            foreach(var item in renames)text.AppendLine("  "+item);
            text.AppendLine();text.AppendLine("Artwork: missing covers will be looked up for confidently matched files. Existing covers remain. Availability is checked when repairs run.");
            text.AppendLine("The plan is rechecked when applied. Duplicate consolidation can reduce the rename list; files changed since this preview can change the plan.");
            text.AppendLine("Originals and rename maps are preserved in the library recovery folder. Automatic rollback protects failed renames; this is not a one-click undo for an entire repair.");
            return text.ToString();
        }
    }
}
