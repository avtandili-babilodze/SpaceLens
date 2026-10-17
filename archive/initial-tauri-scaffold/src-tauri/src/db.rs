use crate::model::*;
use rusqlite::{params, Connection, OptionalExtension, Row};
use std::path::Path;

pub type DbResult<T> = Result<T, String>;
pub fn connect(path: &Path) -> DbResult<Connection> {
    let db = Connection::open(path).map_err(|e| e.to_string())?;
    db.busy_timeout(std::time::Duration::from_secs(10)).map_err(|e| e.to_string())?;
    Ok(db)
}
pub fn init(db: &Connection) -> DbResult<()> {
    db.execute_batch("PRAGMA journal_mode=WAL;
        CREATE TABLE IF NOT EXISTS snapshots(id INTEGER PRIMARY KEY, root TEXT NOT NULL, created INTEGER NOT NULL, status TEXT NOT NULL, size INTEGER DEFAULT 0, files INTEGER DEFAULT 0, folders INTEGER DEFAULT 0, errors INTEGER DEFAULT 0);
        CREATE TABLE IF NOT EXISTS entries(snapshot INTEGER NOT NULL, path TEXT NOT NULL, parent TEXT NOT NULL, name TEXT NOT NULL, kind TEXT NOT NULL, size INTEGER NOT NULL, modified INTEGER NOT NULL, extension TEXT NOT NULL, category TEXT NOT NULL, risk TEXT NOT NULL, reason TEXT NOT NULL, files INTEGER NOT NULL, folders INTEGER NOT NULL, warning TEXT, PRIMARY KEY(snapshot,path));
        CREATE INDEX IF NOT EXISTS entries_parent ON entries(snapshot,parent,kind);
        CREATE INDEX IF NOT EXISTS entries_size ON entries(snapshot,kind,size DESC);
        CREATE INDEX IF NOT EXISTS entries_path ON entries(snapshot,path COLLATE NOCASE);
        UPDATE snapshots SET status='interrupted' WHERE status='scanning';")
        .map_err(|e| e.to_string())
}
pub fn entry_row(row: &Row<'_>) -> rusqlite::Result<Entry> {
    Ok(Entry { path: row.get(0)?, parent: row.get(1)?, name: row.get(2)?, kind: row.get(3)?, size: row.get(4)?, modified: row.get(5)?, extension: row.get(6)?, category: row.get(7)?, risk: row.get(8)?, reason: row.get(9)?, files: row.get(10)?, folders: row.get(11)?, warning: row.get(12)? })
}
pub const COLUMNS: &str = "path,parent,name,kind,size,modified,extension,category,risk,reason,files,folders,warning";
pub fn insert(db: &Connection, id: i64, e: &Entry) -> DbResult<()> {
    db.execute("INSERT INTO entries VALUES (?1,?2,?3,?4,?5,?6,?7,?8,?9,?10,?11,?12,?13,?14)", params![id,e.path,e.parent,e.name,e.kind,e.size,e.modified,e.extension,e.category,e.risk,e.reason,e.files,e.folders,e.warning]).map_err(|e| e.to_string())?;
    Ok(())
}
pub fn entry(db: &Connection, id: i64, path: &str) -> DbResult<Entry> {
    db.query_row(&format!("SELECT {COLUMNS} FROM entries WHERE snapshot=?1 AND path=?2"), params![id,path], entry_row).map_err(|_| "This item is not in the selected scan.".to_owned())
}
pub fn snapshot(db: &Connection, id: i64) -> DbResult<Snapshot> {
    db.query_row("SELECT id,root,created,status,size,files,folders,errors FROM snapshots WHERE id=?1", [id], snapshot_row).map_err(|e| e.to_string())
}
fn snapshot_row(r: &Row<'_>) -> rusqlite::Result<Snapshot> {
    Ok(Snapshot { id:r.get(0)?, root:r.get(1)?, created:r.get(2)?, status:r.get(3)?, size:r.get(4)?, files:r.get(5)?, folders:r.get(6)?, errors:r.get(7)? })
}
pub fn history(db: &Connection) -> DbResult<Vec<Snapshot>> {
    let mut stmt=db.prepare("SELECT id,root,created,status,size,files,folders,errors FROM snapshots ORDER BY id DESC LIMIT 100").map_err(|e| e.to_string())?;
    let rows=stmt.query_map([], snapshot_row).map_err(|e| e.to_string())?;
    rows.collect::<rusqlite::Result<Vec<_>>>().map_err(|e| e.to_string())
}
pub fn prefix(path: &str) -> String {
    let sep = if path.contains('\\') { "\\" } else { "/" };
    format!("{}{}%", path.trim_end_matches(['\\','/']).replace('|', "||").replace('%', "|%").replace('_', "|_"), sep)
}
pub fn aggregate(db: &Connection, id: i64, path: &str) -> DbResult<()> {
    db.execute("UPDATE entries SET size=(SELECT COALESCE(SUM(size),0) FROM entries c WHERE c.snapshot=?1 AND c.parent=?2), files=(SELECT COALESCE(SUM(CASE WHEN kind='file' THEN 1 ELSE files END),0) FROM entries c WHERE c.snapshot=?1 AND c.parent=?2), folders=(SELECT COALESCE(SUM(CASE WHEN kind='directory' THEN 1+folders ELSE 0 END),0) FROM entries c WHERE c.snapshot=?1 AND c.parent=?2) WHERE snapshot=?1 AND path=?2", params![id,path]).map_err(|e| e.to_string())?;
    Ok(())
}
#[allow(clippy::too_many_arguments)]
pub fn children(db: &Connection, id: i64, path: &str, query: &str, sort: &str, descending: bool, filter: &str, offset: u64) -> DbResult<Page> {
    entry(db,id,path)?;
    let sort=match sort { "name"=>"name COLLATE NOCASE", "modified"=>"modified", "type"=>"category", "risk"=>"risk", _=>"size" };
    let order=if descending { "DESC" } else { "ASC" };
    let pattern=format!("%{}%",query.replace('|',"||").replace('%',"|%").replace('_',"|_"));
    let scope="snapshot=?1 AND parent=?2 AND name LIKE ?3 ESCAPE '|' AND (?4='all' OR (?4='directories' AND kind='directory') OR (?4='files' AND kind='file') OR (?4='large' AND kind='file' AND size>=1073741824) OR (?4='review' AND kind='file' AND risk='Review Required'))";
    let total=db.query_row(&format!("SELECT COUNT(*) FROM entries WHERE {scope}"), params![id,path,pattern,filter], |r|r.get(0)).map_err(|e|e.to_string())?;
    let mut stmt=db.prepare(&format!("SELECT {COLUMNS} FROM entries WHERE {scope} ORDER BY (kind='directory') DESC,{sort} {order},path ASC LIMIT 200 OFFSET ?5")).map_err(|e|e.to_string())?;
    let entries=stmt.query_map(params![id,path,pattern,filter,offset],entry_row).map_err(|e|e.to_string())?.collect::<rusqlite::Result<Vec<_>>>().map_err(|e|e.to_string())?;
    Ok(Page {entries,total})
}
pub fn details(db: &Connection, id: i64, path: &str) -> DbResult<Details> {
    let e=entry(db,id,path)?;
    let pattern=prefix(path);
    let scope="snapshot=?1 AND kind='file' AND (path=?2 OR path LIKE ?3 ESCAPE '|')";
    let mut stmt=db.prepare(&format!("SELECT category,SUM(size),COUNT(*) FROM entries WHERE {scope} GROUP BY category ORDER BY SUM(size) DESC")).map_err(|e|e.to_string())?;
    let categories=stmt.query_map(params![id,path,pattern],|r|Ok(Category {name:r.get(0)?,size:r.get(1)?,count:r.get(2)?})).map_err(|e|e.to_string())?.collect::<rusqlite::Result<Vec<_>>>().map_err(|e|e.to_string())?;
    let mut stmt=db.prepare(&format!("SELECT {COLUMNS} FROM entries WHERE {scope} ORDER BY size DESC LIMIT 6")).map_err(|e|e.to_string())?;
    let largest=stmt.query_map(params![id,path,pattern],entry_row).map_err(|e|e.to_string())?.collect::<rusqlite::Result<Vec<_>>>().map_err(|e|e.to_string())?;
    let (review_bytes,large_files)=db.query_row(&format!("SELECT COALESCE(SUM(CASE WHEN risk='Review Required' THEN size ELSE 0 END),0),COALESCE(SUM(CASE WHEN size>=1073741824 THEN 1 ELSE 0 END),0) FROM entries WHERE {scope}"),params![id,path,pattern],|r|Ok((r.get(0)?,r.get(1)?))).map_err(|e|e.to_string())?;
    Ok(Details {entry:e,categories,largest,review_bytes,large_files})
}
pub fn compare(db: &Connection, before: i64, after: i64) -> DbResult<Comparison> {
    let a=snapshot(db,before)?; let b=snapshot(db,after)?;
    if before==after || a.root!=b.root || a.status!="complete" || b.status!="complete" { return Err("Select two different completed scans of the same location.".into()); }
    let mut stmt=db.prepare("SELECT n.path,n.kind,COALESCE(o.size,0),n.size,n.size-COALESCE(o.size,0),CASE WHEN o.path IS NULL THEN 'New' ELSE 'Changed' END FROM entries n LEFT JOIN entries o ON o.snapshot=?1 AND o.path=n.path WHERE n.snapshot=?2 AND (o.path IS NULL OR n.size!=o.size) UNION ALL SELECT o.path,o.kind,o.size,0,-o.size,'Removed' FROM entries o LEFT JOIN entries n ON n.snapshot=?2 AND n.path=o.path WHERE o.snapshot=?1 AND n.path IS NULL ORDER BY 5 DESC LIMIT 200").map_err(|e|e.to_string())?;
    let changes=stmt.query_map(params![before,after],|r|Ok(Change {path:r.get(0)?,kind:r.get(1)?,before:r.get(2)?,after:r.get(3)?,delta:r.get(4)?,change:r.get(5)?})).map_err(|e|e.to_string())?.collect::<rusqlite::Result<Vec<_>>>().map_err(|e|e.to_string())?;
    Ok(Comparison {delta:b.size as i64-a.size as i64, changes})
}
pub fn previous(db:&Connection, current:i64)->DbResult<Option<i64>> {
    db.query_row("SELECT id FROM snapshots WHERE id<?1 AND status='complete' AND root=(SELECT root FROM snapshots WHERE id=?1) ORDER BY id DESC LIMIT 1",[current],|r|r.get(0)).optional().map_err(|e|e.to_string())
}
