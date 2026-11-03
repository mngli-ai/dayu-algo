#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""把 Build/ 里的 Windows 试玩包发布成 GitHub Release，并保证 `releases/latest` 就是它。

为什么用「semver 递增的新 tag」而不是一个叫 latest 的滚动 tag：
    GitHub 的 `/releases/latest` 按语义化版本号取最大的那个（草稿和预发布除外）。
    仓库里已经有人手发的 v0.1.0，一个叫 `latest` 的非语义标签未必能赢过它，页面就会继续指向旧包。
    所以这里读全部 release 找出最大版本、把补丁号 +1 发新 tag，`releases/latest` 才必然解析到刚发的这一版。
资产名固定 `dayu-windows-x64.zip`，于是这条永久链接永远指向最新构建：
    https://github.com/mngli-ai/dayu-algo/releases/latest/download/dayu-windows-x64.zip

用法：
    python tools/publish_release.py                       # 打包 + 发布（补丁号 +1）
    python tools/publish_release.py --pack-only           # 只打 zip，不碰网络
    python tools/publish_release.py --check-only          # 只看 releases/latest 现在解析到哪个 release
    python tools/publish_release.py --bump minor          # 次版本号 +1（新增大功能那一轮）
    python tools/publish_release.py --tag v0.2.0          # 指定版本号发布
    python tools/publish_release.py --min-mtime "2026-11-04 00:00"   # 包内最新文件比它还旧就直接失败，防误发上一轮的包

发完必复核：`/releases/latest` 的 tag 必须等于刚发的 tag，且它的资产里必须有 `dayu-windows-x64.zip`，
任一条不满足就报错退出——不靠「应该没问题」交差。
令牌只从 `git credential fill` 取，绝不打印、绝不写进任何文件。
"""
import argparse
import hashlib
import json
import os
import subprocess
import sys
import time
import urllib.error
import urllib.request
import zipfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BUILD = os.path.join(ROOT, "Build")
OWNER, REPO = "mngli-ai", "dayu-algo"
ASSET_NAME = "dayu-windows-x64.zip"
API = "https://api.github.com"
UPLOAD = "https://uploads.github.com"

# Unity Windows 玩家包必需的条目：少任何一个都不许发布（缺 Data 目录的包下下来只是打不开的空壳）
REQUIRED = ["dayu.exe", "dayu_Data", "UnityPlayer.dll", "MonoBleedingEdge"]
# 除必需项外 Build/ 下还可能有这些；白名单之外的一律不进包（验收截图和日志也都躺在 Build/ 里）
OPTIONAL = ["D3D12", "GameAssembly.dll", "dayu.x86_64", "UnityCrashHandler64.exe"]


def sh(args, cwd=ROOT, inp=None):
    p = subprocess.run(args, cwd=cwd, input=inp, capture_output=True, text=True,
                       encoding="utf-8", errors="replace", timeout=120)
    if p.returncode != 0:
        raise RuntimeError("命令失败：" + " ".join(args) + "\n" + (p.stderr or ""))
    return p.stdout


def git_head():
    out = sh(["git", "rev-parse", "HEAD"]).strip()
    if len(out) != 40:
        raise RuntimeError("git rev-parse HEAD 结果不像 sha：" + out)
    return out


def token():
    # 让 git 的凭据助手给令牌；只取 password= 那一行，绝不打印它的值
    out = sh(["git", "credential", "fill"], inp="protocol=https\nhost=github.com\n\n")
    for line in out.splitlines():
        if line.startswith("password="):
            v = line[len("password="):].strip()
            if v:
                return v
    raise RuntimeError("git credential fill 没给出 github.com 的令牌")


def sha256_file(path):
    m = hashlib.sha256()
    with open(path, "rb") as f:
        for blk in iter(lambda: f.read(1 << 20), b""):
            m.update(blk)
    return m.hexdigest()


def human(n):
    for unit in ("B", "KB", "MB", "GB"):
        if n < 1024 or unit == "GB":
            return "%dB" % n if unit == "B" else "%.1f%s" % (n, unit)
        n /= 1024.0


def selected_names():
    """真正会进包的顶层条目：必需的都在（否则拒绝），可选的有就带上。"""
    missing = [n for n in REQUIRED if not os.path.exists(os.path.join(BUILD, n))]
    if missing:
        raise RuntimeError("Build/ 里缺必需条目，拒绝打包：" + "、".join(missing))
    return [n for n in REQUIRED + OPTIONAL if os.path.exists(os.path.join(BUILD, n))]


def newest_mtime(names):
    """包内最新一个文件的时间，也就是这一版真正的构建时间。

    不能拿 Build/dayu.exe 的时间戳当构建时间：它是 Unity 的玩家模板，增量构建改的是
    dayu_Data/Managed/*.dll 和 dayu_Data/Resources/*，exe 本身原地不动（实测停在 2026-10-29，
    而同一批 DLL 是当天凌晨的）。用它判新鲜度会把刚构建的包判成旧的，
    反过来旧 dayu_Data 又能顶着「新 exe」蒙过闸门——两个方向都错。
    """
    best, best_path = 0.0, ""
    for name in names:
        top = os.path.join(BUILD, name)
        if os.path.isfile(top):
            t = os.path.getmtime(top)
            if t >= best:
                best, best_path = t, name
            continue
        for dirpath, _, filenames in os.walk(top):
            for f in filenames:
                t = os.path.getmtime(os.path.join(dirpath, f))
                if t >= best:
                    best, best_path = t, os.path.relpath(os.path.join(dirpath, f), BUILD)
    return best, best_path


def build_zip(zip_path):
    names = selected_names()
    n_files, total = 0, 0
    if os.path.exists(zip_path):
        os.remove(zip_path)
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        for name in names:
            top = os.path.join(BUILD, name)
            if os.path.isfile(top):
                z.write(top, name)
                n_files += 1
                total += os.path.getsize(top)
                continue
            for dirpath, _, filenames in os.walk(top):
                for f in filenames:
                    full = os.path.join(dirpath, f)
                    z.write(full, os.path.relpath(full, BUILD).replace("\\", "/"))
                    n_files += 1
                    total += os.path.getsize(full)
    size = os.path.getsize(zip_path)
    digest = sha256_file(zip_path)
    print("[pack] 入选顶层条目：" + "、".join(names))
    print("[pack] 文件 %d 个 · 原始 %s · 压缩包 %s" % (n_files, human(total), human(size)))
    print("[pack] sha256 " + digest[:16] + "… → " + os.path.basename(zip_path))
    return size, digest


def api(method, path, tok, payload=None, host=API, body=None, content_type="application/json"):
    data = body if body is not None else (json.dumps(payload).encode("utf-8") if payload is not None else None)
    req = urllib.request.Request(host + path, data=data, method=method)
    req.add_header("Authorization", "token " + tok)
    req.add_header("Accept", "application/vnd.github+json")
    req.add_header("X-GitHub-Api-Version", "2022-11-28")
    if data is not None:
        req.add_header("Content-Type", content_type)
    try:
        with urllib.request.urlopen(req, timeout=300) as r:
            raw = r.read()
            if "json" in (r.headers.get("Content-Type") or ""):
                return r.status, json.loads(raw) if raw else None
            return r.status, raw
    except urllib.error.HTTPError as e:
        raw = e.read()
        try:
            msg = json.loads(raw).get("message", "")
        except Exception:
            msg = raw.decode("utf-8", "replace")[:200]
        return e.code, {"message": msg}


def parse_semver(tag):
    s = (tag or "").strip()
    if s.startswith("v"):
        s = s[1:]
    parts = s.split(".")
    if len(parts) != 3:
        return None
    try:
        return tuple(int(p) for p in parts)
    except ValueError:
        return None


def max_semver(tok):
    code, res = api("GET", "/repos/%s/%s/releases?per_page=100" % (OWNER, REPO), tok)
    if code != 200:
        raise RuntimeError("release 列表读取失败 %s：%s" % (code, res))
    best, count = None, 0
    for r in res:
        if r.get("draft"):
            continue
        count += 1
        sv = parse_semver(r.get("tag_name"))
        if sv and (best is None or sv > best):
            best = sv
    print("[api] 现有非草稿 release %d 个，最大语义版本 %s" % (count, "v%d.%d.%d" % best if best else "无"))
    return best or (0, 1, 0)


def next_tag(cur, bump):
    if bump == "major":
        return "v%d.%d.%d" % (cur[0] + 1, 0, 0)
    if bump == "minor":
        return "v%d.%d.%d" % (cur[0], cur[1] + 1, 0)
    return "v%d.%d.%d" % (cur[0], cur[1], cur[2] + 1)


def ensure_tag_ref(tok, tag, sha):
    path = "/repos/%s/%s/git/ref/tags/%s" % (OWNER, REPO, tag)
    code, _ = api("GET", path, tok)
    if code == 200:                       # 同版本重发：把 tag 挪到当前提交
        code, res = api("PATCH", "/repos/%s/%s/git/refs/tags/%s" % (OWNER, REPO, tag), tok,
                        {"sha": sha, "force": True})
    else:
        code, res = api("POST", "/repos/%s/%s/git/refs" % (OWNER, REPO), tok,
                        {"ref": "refs/tags/" + tag, "sha": sha})
    if code not in (200, 201):
        raise RuntimeError("tag %s 写入失败 %s：%s" % (tag, code, res))
    print("[api] refs/tags/%s → %s" % (tag, sha[:10]))


def ensure_release(tok, tag, note):
    code, res = api("GET", "/repos/%s/%s/releases/tags/%s" % (OWNER, REPO, tag), tok)
    if code == 200:
        rid = res["id"]
        code2, upd = api("PATCH", "/repos/%s/%s/releases/%s" % (OWNER, REPO, rid), tok,
                         {"tag_name": tag, "name": note["name"], "body": note["body"],
                          "draft": False, "prerelease": False})
        if code2 not in (200, 201):
            raise RuntimeError("release 更新失败 %s：%s" % (code2, upd))
        print("[api] 同版本重发，复用 release #%s" % rid)
        return rid
    code3, new = api("POST", "/repos/%s/%s/releases" % (OWNER, REPO), tok,
                     {"tag_name": tag, "name": note["name"], "body": note["body"],
                      "draft": False, "prerelease": False})
    if code3 not in (200, 201):
        raise RuntimeError("release 创建失败 %s：%s" % (code3, new))
    print("[api] 新建 release #%s（tag %s）" % (new["id"], tag))
    return new["id"]


def drop_old_assets(tok, release_id):
    code, res = api("GET", "/repos/%s/%s/releases/%s/assets" % (OWNER, REPO, release_id), tok)
    if code != 200:
        raise RuntimeError("资产列表读取失败 %s：%s" % (code, res))
    for a in res or []:
        code2, r2 = api("DELETE", a["url"].replace("https://api.github.com", ""), tok)
        print("[api] 删旧资产 %s（#%s）→ %s" % (a["name"], a["id"], code2))
        if code2 not in (204, 200):
            raise RuntimeError("旧资产删除失败 %s：%s" % (code2, r2))


def upload(tok, release_id, zip_path, size):
    with open(zip_path, "rb") as f:
        body = f.read()
    if len(body) != size:
        raise RuntimeError("压缩包在读取时大小变了，中止")
    path = "/repos/%s/%s/releases/%s/assets?name=%s" % (OWNER, REPO, release_id, ASSET_NAME)
    code, res = api("POST", path, tok, host=UPLOAD, body=body, content_type="application/zip")
    if code not in (200, 201):
        raise RuntimeError("资产上传失败 %s：%s" % (code, res))
    print("[api] 已上传 %s（%s）" % (res["name"], human(res["size"])))
    return res


def check_latest(tok):
    code, res = api("GET", "/repos/%s/%s/releases/latest" % (OWNER, REPO), tok)
    if code != 200:
        print("[check] /releases/latest 返回 %s：%s" % (code, res))
        return None
    print("[check] latest → tag=%s name=%s draft=%s prerelease=%s" %
          (res["tag_name"], res.get("name"), res.get("draft"), res.get("prerelease")))
    for a in res.get("assets", []):
        print("[check]   资产 %s · %s · %s" % (a["name"], human(a["size"]), a["browser_download_url"]))
    return res


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--pack-only", action="store_true", help="只打压缩包，不联网")
    ap.add_argument("--check-only", action="store_true", help="只查 releases/latest 解析到哪")
    ap.add_argument("--bump", choices=["major", "minor", "patch"], default="patch")
    ap.add_argument("--tag", help="指定版本号，跳过自动递增")
    ap.add_argument("--min-mtime", help="形如 '2026-11-04 00:00'，包内最新文件比它还旧就拒绝发布")
    ap.add_argument("--zip", default=os.path.join(ROOT, ASSET_NAME))
    args = ap.parse_args()

    names = selected_names()
    built_ts, newest = newest_mtime(names)
    built = time.strftime("%Y-%m-%d %H:%M", time.localtime(built_ts))
    print("[pack] 包内最新文件 %s · 时间 %s" % (newest.replace("\\", "/"), built))
    if args.min_mtime:
        limit = time.mktime(time.strptime(args.min_mtime, "%Y-%m-%d %H:%M"))
        if built_ts < limit:
            raise RuntimeError("构建比 --min-mtime 还旧：先重跑构建再发布，别把上一轮的包发出去")

    tok = token()
    if args.check_only:
        check_latest(tok)
        return

    size, digest = build_zip(args.zip)
    if args.pack_only:
        print("[done] 只打包，未联网。发布请去掉 --pack-only")
        return

    sha = git_head()
    tag = args.tag or next_tag(max_semver(tok), args.bump)
    if not parse_semver(tag):
        raise RuntimeError("版本号不是 vX.Y.Z 形式，GitHub 不会把它算进 latest：" + tag)
    body = "\n".join([
        "Windows x64 试玩包。解压后双击 `dayu.exe` 即可玩，不需要安装 Unity。",
        "",
        "- 构建时间：" + built,
        "- 对应提交：" + sha,
        "- 压缩包 sha256：" + digest,
        "- 永久最新链接：https://github.com/%s/%s/releases/latest/download/%s" % (OWNER, REPO, ASSET_NAME),
        "",
        "主角形象为第三方同人素材（CC BY-NC-SA 4.0，非商业），其余画面与音乐均为程序生成。",
    ])
    note = {"name": "大肥鱼の算法大冒险 " + tag, "body": body}
    ensure_tag_ref(tok, tag, sha)
    rid = ensure_release(tok, tag, note)
    drop_old_assets(tok, rid)
    up = upload(tok, rid, args.zip, size)
    if up["size"] != size:
        raise RuntimeError("上传后的大小和本地不一致：%s vs %s" % (up["size"], size))
    res = check_latest(tok)
    if res is None or res["tag_name"] != tag:
        raise RuntimeError("复核失败：/releases/latest 解析到 %s，不是刚发的 %s" %
                           (res and res["tag_name"], tag))
    names = [a["name"] for a in res.get("assets", [])]
    if ASSET_NAME not in names:
        raise RuntimeError("复核失败：latest 的资产里没有 " + ASSET_NAME)
    print("[done] latest = %s · 永久链接 https://github.com/%s/%s/releases/latest/download/%s"
          % (tag, OWNER, REPO, ASSET_NAME))


if __name__ == "__main__":
    try:
        main()
    except Exception as e:
        print("[fail] " + str(e))
        sys.exit(1)
