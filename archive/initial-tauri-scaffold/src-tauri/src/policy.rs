use std::path::Path;

pub fn category(extension: &str) -> &'static str {
    match extension {
        "mp4" | "mov" | "mkv" | "avi" | "webm" => "Video",
        "jpg" | "jpeg" | "png" | "gif" | "webp" | "heic" | "svg" | "raw" => "Images",
        "mp3" | "wav" | "flac" | "ogg" | "m4a" => "Audio",
        "zip" | "7z" | "rar" | "gz" | "tar" | "iso" => "Archives",
        "exe" | "msi" | "msix" | "dll" | "sys" => "Applications",
        "pdf" | "doc" | "docx" | "txt" | "xlsx" | "pptx" | "csv" | "md" => "Documents",
        "rs" | "ts" | "tsx" | "js" | "jsx" | "py" | "go" | "c" | "cpp" | "json" => "Development",
        _ => "Other",
    }
}

// A preview policy, never authorization to delete. Unknown data defaults to review.
pub fn classify(path: &Path, is_dir: bool, is_link: bool) -> (&'static str, &'static str) {
    let p = path.to_string_lossy().replace('/', "\\").to_lowercase();
    let parts: Vec<&str> = p.split('\\').filter(|s| !s.is_empty()).collect();
    let name = parts.last().copied().unwrap_or("");
    if is_link || parts.iter().any(|v| matches!(*v, "windows" | "program files" | "program files (x86)" | "programdata" | "system volume information" | "$recycle.bin" | "recovery"))
        || matches!(name, "pagefile.sys" | "hiberfil.sys" | "swapfile.sys" | "bootmgr") {
        return ("Protected", "System, installed application, or linked data. Use the owning application's supported management tools.");
    }
    if is_dir {
        return ("Review Required", "Folder contents may contain personal or application data. Review individual files; the folder is not a safe deletion target.");
    }
    let ext = path.extension().and_then(|v| v.to_str()).unwrap_or("").to_lowercase();
    if matches!(ext.as_str(), "sys" | "dll" | "reg" | "vhd" | "vhdx" | "db" | "sqlite" | "pst" | "ost") || parts.contains(&"appdata") {
        return ("High Risk", "May be required application state, a database, or a virtual disk. Manage through its owning application and keep a backup.");
    }
    if parts.iter().any(|v| matches!(*v, "node_modules" | ".git" | ".venv" | "venv")) {
        return ("High Risk", "Development dependency or repository data. Removing it can break environments or lose work.");
    }
    if matches!(ext.as_str(), "exe" | "msi" | "msix" | "zip" | "7z" | "rar" | "iso") {
        return ("Review Required", "Possible installer or archive. Confirm it is replaceable and contains no unique files before removing it.");
    }
    ("Review Required", "Purpose and backup status are unknown. Inspect this file and confirm you no longer need it.")
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn never_trust_system_or_unknown_data() {
        for p in [r"C:\Windows\Temp\a.tmp", r"C:\Program Files\App\cache.bin", r"C:\pagefile.sys"] {
            assert_eq!(classify(Path::new(p), false, false).0, "Protected");
        }
        assert_eq!(classify(Path::new(r"C:\Users\Alex\AppData\cache.tmp"), false, false).0, "High Risk");
        assert_eq!(classify(Path::new(r"C:\Users\Alex\Downloads\setup.exe"), false, false).0, "Review Required");
        assert_eq!(classify(Path::new(r"C:\Users\Alex\Documents"), true, false).0, "Review Required");
        assert_eq!(classify(Path::new(r"C:\data\junction"), true, true).0, "Protected");
    }
}
