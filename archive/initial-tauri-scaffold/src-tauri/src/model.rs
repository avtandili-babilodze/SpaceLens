use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Entry {
    pub path: String,
    pub parent: String,
    pub name: String,
    pub kind: String,
    pub size: u64,
    pub modified: u64,
    pub extension: String,
    pub category: String,
    pub risk: String,
    pub reason: String,
    pub files: u64,
    pub folders: u64,
    pub warning: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Snapshot {
    pub id: i64,
    pub root: String,
    pub created: u64,
    pub status: String,
    pub size: u64,
    pub files: u64,
    pub folders: u64,
    pub errors: u64,
}

#[derive(Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Progress {
    pub files: u64,
    pub folders: u64,
    pub bytes: u64,
    pub errors: u64,
    pub current: String,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Drive { pub path: String, pub name: String, pub total: u64, pub available: u64, pub format: String }

#[derive(Serialize)]
pub struct Page { pub entries: Vec<Entry>, pub total: u64 }

#[derive(Serialize)]
pub struct Category { pub name: String, pub size: u64, pub count: u64 }

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Details {
    pub entry: Entry,
    pub categories: Vec<Category>,
    pub largest: Vec<Entry>,
    pub review_bytes: u64,
    pub large_files: u64,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Plan {
    pub goal: u64,
    pub potential: u64,
    pub candidates: Vec<Entry>,
    pub message: String,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Change {
    pub path: String, pub kind: String, pub before: u64, pub after: u64,
    pub delta: i64, pub change: String,
}

#[derive(Serialize)]
pub struct Comparison { pub delta: i64, pub changes: Vec<Change> }

#[derive(Serialize)]
pub struct Answer { pub text: String, pub entries: Vec<Entry> }
