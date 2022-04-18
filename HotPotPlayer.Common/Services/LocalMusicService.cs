﻿using HotPotPlayer.Extensions;
using HotPotPlayer.Models;
using Microsoft.UI.Dispatching;
using Realms;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.Storage;

namespace HotPotPlayer.Services
{
    public class LocalMusicService: ServiceBaseWithConfig
    {
        public LocalMusicService(ConfigBase config) : base(config) { }
        
        #region State
        public enum LocalMusicState
        {
            Idle,
            FirstLoading,
            NonFirstLoading,
            InitComplete,
            Complete,
            NoLibraryAccess
        }

        private LocalMusicState _state = LocalMusicState.Idle;

        public LocalMusicState State
        {
            get => _state;
            set => Set(ref _state, value);
        }

        #endregion
        #region Property
        private ObservableCollection<AlbumGroup> _localAlbumGroup;

        public ObservableCollection<AlbumGroup> LocalAlbumGroup
        {
            get => _localAlbumGroup ??= new ObservableCollection<AlbumGroup>();
            set => Set(ref _localAlbumGroup, value);
        }

        private ObservableCollection<PlayListItem> _localPlayListList;

        public ObservableCollection<PlayListItem> LocalPlayListList
        {
            get => _localPlayListList;
            set => Set(ref _localPlayListList, value);
        }
        #endregion
        #region Field
        static readonly string[] SupportedExt = new[] { ".flac", ".wav", ".m4a", ".mp3" };

        BackgroundWorker _loader;
        BackgroundWorker Loader
        {
            get
            {
                if (_loader == null)
                {
                    _loader = new BackgroundWorker
                    {
                        WorkerSupportsCancellation = true,
                    };
                    _loader.DoWork += LoadLocalMusic;
                    _loader.RunWorkerCompleted += LoadLocalCompleted;
                }
                return _loader;
            }
        }

        string _dbPath;
        string DbPath => _dbPath ??= Path.Combine(Config.DatabaseFolder, "LocalMusic.db");

        MD5 _md5;
        MD5 Md5 => _md5 ??= MD5.Create();

        DispatcherQueue _UIQueue;

        List<FileSystemWatcher> _watchers;
        #endregion

        static List<MusicItem> GetAllMusicItem(IEnumerable<FileInfo> files)
        {
            return files.Select(FileToMusic).ToList();
        }

        public static MusicItem FileToMusic(FileInfo f)
        {
            using var tfile = TagLib.File.Create(f.FullName);
            //var duration = await GetMusicDurationAsync(f);
            var item = new MusicItem
            {
                Source = f,
                Title = tfile.Tag.Title,
                Artists = tfile.Tag.Performers,
                Album = tfile.Tag.Album,
                Year = (int)tfile.Tag.Year,
                //Duration = duration,
                Duration = tfile.Properties.Duration,
                Track = (int)tfile.Tag.Track,
                LastWriteTime = f.LastWriteTime,
                AlbumArtists = tfile.Tag.AlbumArtists,
                Disc = (int)tfile.Tag.Disc,
            };

            return item;
        }

        static async Task<TimeSpan> GetMusicDurationAsync(FileInfo f)
        {
            var file = await StorageFile.GetFileFromPathAsync(f.FullName);
            var prop = await file?.Properties?.GetMusicPropertiesAsync();
            return prop == null ? TimeSpan.Zero : prop.Duration;
        }

        private List<FileInfo> GetMusicFilesFromLibrary()
        {
            var libs = Config.MusicPlayList.Select(s => s.Path);
            List<FileInfo> files = new();
            foreach (var lib in libs)
            {
                var di = new DirectoryInfo(lib);
                files.AddRange(di.GetFiles("*.*", SearchOption.AllDirectories).Where(f => SupportedExt.Contains(f.Extension)));
            }

            return files;
        }

        List<AlbumItem> GroupAllMusicIntoAlbum(IEnumerable<MusicItem> allMusic)
        {
            var albums = allMusic.GroupBy(m2 => m2.AlbumSignature).Select(g2 => 
            {
                var music = g2.ToList();
                music.Sort((a,b) => a.DiscTrack.CompareTo(b.DiscTrack));

                var (cover, color) = WriteCoverToLocalCache(g2.First());

                var albumArtists = g2.First().AlbumArtists.Length == 0 ? g2.First().Artists : g2.First().AlbumArtists;
                var allArtists = g2.SelectMany(m => m.Artists).Concat(albumArtists).Distinct().ToArray();

                var i = new AlbumItem
                {
                    Title = g2.First().Album,
                    Artists = albumArtists,
                    Year = g2.First().Year,
                    Cover = cover,
                    MusicItems = music,
                    MainColor = color,
                    AllArtists = allArtists
                };
                foreach (var item in i.MusicItems)
                {
                    item.Cover = i.Cover;
                    item.MainColor = i.MainColor;
                    item.AlbumRef = i;
                }
                return i;
            }).ToList();
            return albums;
        }

        static List<AlbumGroup> GroupAllAlbumByYear(IEnumerable<AlbumItem> albums)
        {
            var r = albums.GroupBy(m => m.Year).Select(g => 
            {
                var albumList = g.ToList();
                albumList.Sort((a,b) => (a.Title ?? "").CompareTo(b.Title ?? ""));
                var r = new AlbumGroup()
                {
                    Year = g.Key,
                    Items = new ObservableCollection<AlbumItem>(albumList)
                };
                return r;
            }).ToList();
            r.Sort((a, b) => b.Year.CompareTo(a.Year));
            return r;
        }



        string _albumCoverDir;
        string AlbumCoverDir
        {
            get
            {
                if (string.IsNullOrEmpty(_albumCoverDir))
                {
                    _albumCoverDir = Path.Combine(Config.LocalFolder, "Cover");
                    if (!Directory.Exists(_albumCoverDir))
                    {
                        Directory.CreateDirectory(_albumCoverDir);
                    }
                }
                return _albumCoverDir;
            }
        }

        (string, Color) WriteCoverToLocalCache(MusicItem m)
        {
            if (!string.IsNullOrEmpty(m.Cover))
            {
                return (m.Cover, m.MainColor);
            }
         
            var tag = TagLib.File.Create(m.Source.FullName);
            Span<byte> binary = tag.Tag.Pictures?.FirstOrDefault()?.Data?.Data;

            if (binary != null && binary.Length != 0)
            {
                var buffer = Md5.ComputeHash(binary.ToArray());
                var hashName = Convert.ToHexString(buffer);
                var albumCoverName = Path.Combine(AlbumCoverDir, hashName);

                using var image = Image.Load<Rgba32>(binary);
                var width = image.Width;
                var height = image.Height;
                image.Mutate(x => x.Resize(400, 400*height/width));
                var color = image.GetMainColor();
                image.SaveAsPng(albumCoverName);

                return (albumCoverName, color);
            }
            return (string.Empty, Color.White);
        }

        /// <summary>
        /// 启动加载本地音乐
        /// </summary>
        public void StartLoadLocalMusic()
        {
            _UIQueue ??= DispatcherQueue.GetForCurrentThread();
            if (Loader.IsBusy)
            {
                return;
            }
            Loader.RunWorkerAsync();
        }

        void LoadLocalCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            State = LocalMusicState.Complete;
        }

        void EnqueueChangeState(LocalMusicState newState)
        {
            _UIQueue.TryEnqueue(() =>
            {
                State = newState;
            });
        }

        void LoadLocalMusic(object sender, DoWorkEventArgs e)
        {
            //检查是否有缓存好的数据库
            var libs = Config.MusicLibrary;
            if (libs == null)
            {
                EnqueueChangeState(LocalMusicState.NoLibraryAccess);
                return;
            }

            // 获取DB，如果没有其会自动创建
            using var db = Realm.GetInstance(DbPath);

            // 获取DB内容
            var dbAlbums = db.All<AlbumItemDb>();
            var dbPlayLists = db.All<PlayListItemDb>();

            // 转换为非数据库类型
            var dbAlbumList_ = dbAlbums.AsEnumerable().Select(d => d.ToOrigin()).ToList();
            var dbPlayListList = dbPlayLists.AsEnumerable().Select(d => d.ToOrigin()).ToList();

            // Album分组
            var dbAlbumGroups = GroupAllAlbumByYear(dbAlbumList_);
            _UIQueue.TryEnqueue(() =>
            {
                LocalAlbumGroup = new ObservableCollection<AlbumGroup>(dbAlbumGroups);
                LocalPlayListList = new ObservableCollection<PlayListItem>(dbPlayListList);
                State = LocalMusicState.InitComplete;
            });

            // 查询本地文件变动
            var localMusicFiles = GetMusicFilesFromLibrary();
            var localPlayListFiles = GetAllPlaylistFiles();
            var (removeList, addOrUpdateList) = CheckMusicHasUpdate(db, localMusicFiles);
            var playListHasUpdate = CheckPlayListHasUpdate(dbPlayListList, localPlayListFiles);

            // 应用更改
            ObservableCollection<AlbumGroup> newAlbumGroup = null;
            ObservableCollection<PlayListItem> newPlayListList = null;
            if (addOrUpdateList != null && addOrUpdateList.Any())
            {
                newAlbumGroup = new(AddOrUpdateMusicAndSave(db, addOrUpdateList));
            }
            if (removeList != null && removeList.Any())
            {
                newAlbumGroup = new(RemoveMusicAndSave(db, removeList));
            }
            if (playListHasUpdate)
            {
                newPlayListList = new(ScanAllPlayList(db, localPlayListFiles));
            }

            if (newAlbumGroup != null)
            {
                _UIQueue.TryEnqueue(() =>
                {
                    LocalAlbumGroup = newAlbumGroup;
                });
            };
            if (newPlayListList != null)
            {
                _UIQueue.TryEnqueue(() =>
                {
                    LocalPlayListList = newPlayListList;
                });
            }

            // 最后启动文件系统监控
            InitFileSystemWatcher();
        }

        private List<AlbumGroup> RemoveMusicAndSave(Realm db, IEnumerable<string> removeList)
        {
            db.Write(() =>
            {
                foreach (var item in removeList)
                {
                    db.Remove(db.Find<MusicItemDb>(item));
                }
            });

            var allmusic = db.All<MusicItemDb>().AsEnumerable().Select(d => d.ToOrigin());
            var albums = GroupAllMusicIntoAlbum(allmusic);

            db.Write(() =>
            {
                var existAlbum = db.All<AlbumItemDb>();
                var existMusic = db.All<MusicItemDb>();
                db.RemoveRange(existAlbum);
                db.RemoveRange(existMusic);
                db.Add(albums.Select(a => a.ToDb()));
            });

            var groups = GroupAllAlbumByYear(albums);
            return groups;
        }

        private List<AlbumGroup> AddOrUpdateMusicAndSave(Realm db, IEnumerable<FileInfo> addOrUpdateList)
        {
            List<AlbumGroup> groups;
            var addOrUpdateMusic = GetAllMusicItem(addOrUpdateList);
            db.Write(() =>
            {
                db.Add(addOrUpdateMusic.Select(a => a.ToDb()), update: true);
            });

            var allmusic = db.All<MusicItemDb>().AsEnumerable().Select(d => d.ToOrigin());
            var albums = GroupAllMusicIntoAlbum(allmusic);

            db.Write(() =>
            {
                var existAlbum = db.All<AlbumItemDb>();
                db.RemoveRange(existAlbum);
                db.Add(albums.Select(a => a.ToDb()), update: true);
            });

            groups = GroupAllAlbumByYear(albums);
            return groups;
        }

        sealed class MusicItemComparer : EqualityComparer<MusicItemDb>
        {
            public override bool Equals(MusicItemDb x, MusicItemDb y)
            {
                if (x.Source == y.Source && x.LastWriteTime == y.LastWriteTime)
                    return true;
                return false;
            }

            public override int GetHashCode(MusicItemDb obj)
            {
                return obj.Source.GetHashCode() + obj.LastWriteTime.GetHashCode();
            }
        }

        private static (IEnumerable<string> removeList, IEnumerable<FileInfo> addOrUpdateList) CheckMusicHasUpdate(Realm db, List<FileInfo> files)
        {
            var currentFiles = files.Select(c => new MusicItemDb
            {
                Source = c.FullName,
                LastWriteTime = c.LastWriteTime.ToBinary()
            });
            var dbFiles = db.All<MusicItemDb>().ToList();

            var newFiles = currentFiles.Except(dbFiles, new MusicItemComparer());
            var exc2 = newFiles.Where(d => Directory.Exists(Path.GetPathRoot(d.Source)))
                .Select(s => new FileInfo(s.Source));

            var removeFileKeys = dbFiles.Except(currentFiles, new MusicItemComparer())
                .Select(d => d.Source);

            return (removeFileKeys, exc2);
        }

        private List<PlayListItem> ScanAllPlayList(Realm db, List<FileInfo> playListsFile)
        {
            var playLists = GetAllPlaylistItem(db, playListsFile);
            db.Write(() =>
            {
                var exist = db.All<PlayListItemDb>();
                db.RemoveRange(exist);
                db.Add(playLists.Select(a => a.ToDb()), update: true);
            });
            return playLists;
        }


        private static bool CheckPlayListHasUpdate(List<PlayListItem> stored, List<FileInfo> current)
        {
            foreach (var s in stored)
            {
                var match = current.FirstOrDefault(f => f.FullName == s.Source.FullName);
                if (match != null && match.LastWriteTime != s.LastWriteTime)
                {
                    return true;
                }
            }
            return false;
        }

        private List<FileInfo> GetAllPlaylistFiles()
        {
            var libs = Config.MusicPlayList.Select(s => s.Path);
            List<FileInfo> files = new();
            foreach (var lib in libs)
            {
                var di = new DirectoryInfo(lib);
                if (!di.Exists) continue;
                files.AddRange(di.GetFiles("*.zpl", SearchOption.AllDirectories));
            }
            return files;
        }

        private List<PlayListItem> GetAllPlaylistItem(Realm db, List<FileInfo> files)
        {
            var r = files.Select(f =>
            {
                var ost_doc = XDocument.Load(f.FullName);
                var smil = ost_doc.Root;
                var body = smil.Elements().FirstOrDefault(n => n.Name == "body");
                var head = smil.Elements().FirstOrDefault(n => n.Name == "head");
                var title = head.Elements().FirstOrDefault(n => n.Name == "title").Value;
                var seq = body.Elements().FirstOrDefault(n => n.Name == "seq");
                var srcs = seq.Elements().Select(m => m.Attribute("src").Value);
                var files = srcs.Select(path =>
                {
                    var musicFromDb = db.All<MusicItemDb>().Where(d => d.Source == path).FirstOrDefault();
                    var origin = musicFromDb?.ToOrigin();
                    return origin;
                }).ToList();
                files.RemoveAll(f => f == null);

                var pl = new PlayListItem
                {
                    Source = f,
                    Title = title,
                    Year = f.LastWriteTime.Year,
                    LastWriteTime = f.LastWriteTime,
                    MusicItems = files,
                };
                pl.SetPlayListCover(Config);
                return pl;
            }).ToList();

            return r;
        }

        #region FileSystemWatcher
        private void InitFileSystemWatcher()
        {
            _watchers ??= Config.MusicLibrary.Select(l =>
            {
                var fsw = new FileSystemWatcher
                {
                    Path = l.Path,
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                };

                fsw.Created += WatcherMusicCreated;
                fsw.Renamed += WatcherMusicRenamed;
                fsw.Deleted += WatcherMusicDeleted;

                fsw.EnableRaisingEvents = true;
                return fsw;
            }).ToList();
        }

        private void WatcherMusicDeleted(object sender, FileSystemEventArgs e)
        {
            if (!Loader.IsBusy)
            {
                Loader.RunWorkerAsync();
            }
        }

        private void WatcherMusicRenamed(object sender, RenamedEventArgs e)
        {
            if (!Loader.IsBusy)
            {
                Loader.RunWorkerAsync();
            }
        }

        private void WatcherMusicCreated(object sender, FileSystemEventArgs e)
        {
            if (!Loader.IsBusy)
            {
                Loader.RunWorkerAsync();
            }
        }
        #endregion

        List<AlbumItem> QueryArtistAlbum(string artistName)
        {
            using var db = Realm.GetInstance(DbPath);
            var result = db.All<AlbumItemDb>().Where(a => a.AllArtists.Contains(artistName)).AsEnumerable().Select(d => d.ToOrigin()).ToList();
            return result;
        }

        public (List<AlbumGroup>, List<MusicItem>) GetArtistAlbumGroup(string artistName)
        {
            var album = QueryArtistAlbum(artistName);
            var group = GroupAllAlbumByYear(album);
            var music = album.SelectMany(a => a.MusicItems).ToList();
            return (group, music);
        }

        public AlbumItem QueryAlbum(MusicItem musicItem)
        {
            using var db = Realm.GetInstance(DbPath);
            var musicDb = db.Find<MusicItemDb>(musicItem.GetKey());
            var album = musicDb.GetAlbum();
            return album;
        }

        public override void Dispose()
        {
            Loader?.Dispose();
            _watchers?.ForEach(fsw => fsw.Dispose());
        }
    }
}
