﻿// zlib/libpng License
//
// Copyright (c) 2018 Sinoa
//
// This software is provided 'as-is', without any express or implied warranty.
// In no event will the authors be held liable for any damages arising from the use of this software.
// Permission is granted to anyone to use this software for any purpose,
// including commercial applications, and to alter it and redistribute it freely,
// subject to the following restrictions:
//
// 1. The origin of this software must not be misrepresented; you must not claim that you wrote the original software.
//    If you use this software in a product, an acknowledgment in the product documentation would be appreciated but is not required.
// 2. Altered source versions must be plainly marked as such, and must not be misrepresented as being the original software.
// 3. This notice may not be removed or altered from any source distribution.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IceMilkTea.Core;
using IceMilkTea.SubSystem;
using UnityEngine;

namespace IceMilkTea.Service
{
    /// <summary>
    /// AssetBundleStorageController を使って高レベル層の管理を行うマネージャクラスです。
    /// 主に、アセットバンドルキャッシュと、アセットバンドルの依存関係を解決を行います。
    /// </summary>
    internal class AssetBundleStorageManager
    {
        /// <summary>
        /// アセットバンドルの管理状況を保持したコンテキストクラスです
        /// </summary>
        private class AssetBundleManagementContext
        {
            // メンバ変数定義
            private AssetBundle assetBundle;



            /// <summary>
            /// アセットバンドルの参照カウント値を取得します
            /// </summary>
            public int ReferenceCount { get; private set; }


            /// <summary>
            /// アセットバンドルへの参照がどこからも無くなっているかどうか
            /// </summary>
            public bool Unreferenced => ReferenceCount == 0;



            /// <summary>
            /// AssetBundleManagementContext のインスタンスを初期化します
            /// </summary>
            /// <param name="targetAssetBundle">管理対象となるアセットバンドル</param>
            /// <exception cref="ArgumentNullException">targetAssetBundle が null です</exception>
            public AssetBundleManagementContext(AssetBundle targetAssetBundle)
            {
                // もしnullを渡されたら
                if (targetAssetBundle == null)
                {
                    // 何を管理すればよいのだ
                    throw new ArgumentNullException(nameof(targetAssetBundle));
                }


                // 初期化をする
                assetBundle = targetAssetBundle;
                ReferenceCount = 1;
            }


            /// <summary>
            /// 管理しているアセットバンドルを取得します
            /// </summary>
            /// <returns>管理しているアセットバンドルを返します</returns>
            public AssetBundle GetAssetBundle()
            {
                // 参照カウントをインクリメントして参照を返す
                ++ReferenceCount;
                return assetBundle;
            }


            /// <summary>
            /// 対象のアセットバンドルの参照を破棄します
            /// </summary>
            /// <param name="assetBundle">破棄するアセットバンドルの参照</param>
            /// <exception cref="ArgumentException">解放しようとしたアセットバンドルの参照が異なっています。Managed={this.assetBundle.name} Request={assetBundle.name}</exception>
            /// <exception cref="InvalidOperationException">既に解放済みのアセットバンドルを更に解放しようとしました</exception>
            public void ReleaseAssetBundle(AssetBundle assetBundle)
            {
                // もし渡された参照と管理対象のアセットバンドルの参照が異なるなら
                if (!EqualAssetBundleReference(assetBundle))
                {
                    // これは間違った解放をしようとしている
                    throw new ArgumentException($"解放しようとしたアセットバンドルの参照が異なっています。Managed={this.assetBundle.name} Request={assetBundle.name}", nameof(assetBundle));
                }


                // 参照カウントが既に0なら
                if (ReferenceCount == 0)
                {
                    // 必要以上なReleaseが呼び出された可能性がある
                    throw new InvalidOperationException("既に解放済みのアセットバンドルを更に解放しようとしました");
                }


                // 問題なければ参照カウントをデクリメントする
                --ReferenceCount;
            }


            /// <summary>
            /// 指定されたアセットバンドルの参照と一致するかどうか確認をします
            /// </summary>
            /// <param name="assetBundle">参照の確認を行うアセットバンドル</param>
            /// <returns>参照が一致していれば true を、一致していなければ false を返します</returns>
            public bool EqualAssetBundleReference(AssetBundle assetBundle)
            {
                // 素直に参照の比較結果を返す
                return this.assetBundle == assetBundle;
            }
        }



        // 定数定義
        private const int DefaultAssetBundleTableCapacity = 2 << 10;

        // メンバ変数定義
        private AssetBundleManifestManager manifestManager;
        private AssetBundleStorageController storageController;
        private AssetBundleInstaller installer;
        private Dictionary<string, Task<AssetBundleManagementContext>> assetBundleTable;

        private readonly IAssetStorage storage;
        private readonly ImtAssetDatabase assetDatabase;


        /// <summary>
        /// AssetBundleStorageManager のインスタンスを初期化します
        /// </summary>
        /// <param name="manifestManager">パス解決などに利用するマニフェストマネージャ</param>
        /// <param name="storageController">アセットバンドルの入出力を実際に行うコントローラ</param>
        /// <param name="installer">アセットバンドルをインストールするインストーラ</param>
        /// <exception cref="ArgumentNullException">manifestManager が null です</exception>
        /// <exception cref="ArgumentNullException">storageController が null です</exception>
        public AssetBundleStorageManager(AssetBundleManifestManager manifestManager, AssetBundleStorageController storageController, AssetBundleInstaller installer, IAssetStorage storage, ImtAssetDatabase assetDatabase)
        {
            // null を渡されたら
            if (manifestManager == null)
            {
                // マニフェストが無いとパス解決が出来ない
                throw new ArgumentNullException(nameof(manifestManager));
            }


            // null を渡されたら
            if (storageController == null)
            {
                // 流石にアセットバンドルの制御が出来ないとむり
                throw new ArgumentNullException(nameof(storageController));
            }


            // null を渡されたら
            if (installer == null)
            {
                // 流石にどこからも引っ張ってこれないのはむり
                throw new ArgumentNullException(nameof(installer));
            }


            // 受け取る
            this.manifestManager = manifestManager;
            this.storageController = storageController;
            this.installer = installer;

            this.storage = storage;
            this.assetDatabase = assetDatabase;


            // 管理テーブルを生成する
            assetBundleTable = new Dictionary<string, Task<AssetBundleManagementContext>>(DefaultAssetBundleTableCapacity);
        }


        /// <summary>
        /// 指定されたアセットバンドル情報から、アセットバンドルが存在するかを確認します
        /// </summary>
        /// <param name="info">確認するアセットバンドル情報</param>
        /// <returns>存在することが確認できた場合は true を、存在を確認出来なかった場合は false を返します</returns>
        public bool ExistsAssetBundle(AssetBundleInfo info)
        {
            // コントローラに存在確認をした結果を返す
            return storageController.Exists(info);
        }

        public bool ExistsAssetBundle(ICatalogItem item)
        {
            return this.storage.ExistsAsset(item.LocalUri);
        }


        /// <summary>
        /// 指定されたアセットバンドル情報のアセットバンドルを非同期でインストールします
        /// </summary>
        /// <param name="info">インストールを行うアセットバンドル情報</param>
        /// <param name="progress">インストールの進捗通知を受ける Progress。通知が不要の場合は null の指定が可能です。</param>
        /// <returns>非同期でインストールしているタスクを返します</returns>
        [Obsolete]
        public async Task InstallAssetBundleAsync(AssetBundleInfo info, IProgress<AssetBundleInstallProgress> progress)
        {
            // 進捗通知を行うプログレスの生成を行う
            var status = AssetBundleInstallStatus.HashCheck;
            var progressScale = 0.5;
            var progressOffset = 0.0;
            var internalProgress = new Progress<double>(x =>
            {
                // 進捗通知用パラメータを生成して通知する
                var progressParameter = new AssetBundleInstallProgress(status, info.Name, progressOffset + x * progressScale);
                progress?.Report(progressParameter);
            });


            // ストレージコントローラから指定されたアセットバンドルが存在していてかつベリファイが通るなら
            if (storageController.Exists(info) && await storageController.VerifyAsync(info, internalProgress))
            {
                // この時点でインストール済みと判断して通知を出して終了
                progress?.Report(new AssetBundleInstallProgress(AssetBundleInstallStatus.Installing, info.Name, 1.0));
                return;
            }


            // インストールステータスにする
            status = AssetBundleInstallStatus.Installing;
            progressOffset = 0.5;


            // ストレージコントローラからインストールストリームをもらってインストールを行う
            var installStream = await storageController.GetInstallStreamAsync(info);
            await installer.InstallAsync(info, installStream, internalProgress);
            installStream.Close();
        }


        /// <summary>
        /// 指定されたコンテンツグループに所属するアセットバンドルを非同期でインストールします
        /// </summary>
        /// <param name="groupName">インストールするコンテンツグループ名</param>
        /// <param name="progress">インストールの進捗通知を受ける Progress。通知が不要の場合は null の指定が可能です。</param>
        /// <returns>非同期でインストールしているタスクを返します</returns>
        [Obsolete]
        public async Task InstallContentGroupAsync(string groupName, IProgress<AssetBundleInstallProgress> progress)
        {
            // もしグループ名がnullなら
            if (groupName == null)
            {
                // どのグループをインストールすればよいのか
                throw new ArgumentNullException(nameof(groupName));
            }


            // マニフェストからグループ情報を取り出す
            AssetBundleContentGroup contentGroup;
            manifestManager.GetContentGroupInfo(groupName, out contentGroup);


            // コンテンツグループ内に収録されているアセットバンドル情報分回る
            var assetBundleInfos = contentGroup.AssetBundleInfos;
            for (int i = 0; i < assetBundleInfos.Length; ++i)
            {
                // アセットバンドルインストールを非同期で行う
                await InstallAssetBundleAsync(assetBundleInfos[i], progress);
            }
        }


        /// <summary>
        /// マニフェストとして管理されているすべてのアセットバンドルを非同期でインストールします
        /// </summary>
        /// <param name="progress">インストールの進捗通知を受ける Progress。通知が不要の場合は null の指定が可能です。</param>
        /// <returns>非同期でインストールしているタスクを返します</returns>
        [Obsolete]
        public async Task InstallAllAsync(IProgress<AssetBundleInstallProgress> progress)
        {
            // コンテンツグループ名すべて取得してコンテンツグループすべて回る
            foreach (var contentGroupName in manifestManager.GetContentGroupNames())
            {
                // コンテンツグループのインストールを非同期で行う
                await InstallContentGroupAsync(contentGroupName, progress);
            }
        }

        /// <summary>
        /// 新規/更新/削除の情報に基づいてアセットバンドルをダウンロードないし削除します
        /// </summary>
        /// <param name="updates">新規/更新/削除の情報</param>
        /// <param name="progress">インストールの進捗通知を受ける Progress。通知が不要の場合は null の指定が可能です。</param>
        /// <returns>非同期でインストールしているタスクを返します</returns>
        public async Task InstallUpdatedAsync(IReadOnlyList<UpdatableAssetBundleInfo> updates, IProgress<AssetBundleInstallProgress> progress)
        {
            for(int i = 0; i < updates.Count; i++)
            {
                var item = updates[i];
                if(item.UpdateType == AssetBundleUpdateType.Remove)
                {
                    await RemoveAsync(item.AssetBundleInfo);
                }
                else //NewOrUpdated
                {
                    await InstallAssetBundleAsync(item.AssetBundleInfo, progress);
                }
            }
        }

        /// <summary>
        /// 指定されたアセットバンドル情報のアセットバンドルを、非同期に削除します。
        /// </summary>
        /// <param name="info">削除するアセットバンドルの情報</param>
        /// <returns>アセットバンドルの削除を非同期に操作しているタスクを返します</returns>
        [Obsolete]
        public Task RemoveAsync(AssetBundleInfo info)
        {
            // コントローラに削除を要求して返す
            return storageController.RemoveAsync(info);
        }


        /// <summary>
        /// AssetBundleStorageController が管理しているアセットバンドル全てを非同期で削除します
        /// </summary>
        /// <param name="progress">削除の進捗通知を受ける Progress。もし通知を受けない場合は null の指定が可能です</param>
        /// <returns>アセットバンドルの削除を非同期で行っているタスクを返します</returns>
        [Obsolete]
        public Task RemoveAllAsync(IProgress<double> progress)
        {
            // コントローラに全削除を要求して返す
            return storageController.RemoveAllAsync(progress);
        }


        /// <summary>
        /// 指定されたアセットバンドル情報のアセットバンドルを非同期で開きます。
        /// また、アセットバンドルの依存解決も同時に行い、依存するアセットバンドルも非同期で開きます。
        /// </summary>
        /// <param name="info">アセットバンドルとして開くアセットバンドル情報</param>
        /// <returns>アセットバンドルを非同期で開くタスクを返します</returns>
        /// <exception cref="InvalidOperationException">アセットバンドル '{info.Name}' が依存する、アセットバンドル '{dependenceAssetBundleName}' の情報が見つかりませんでした</exception>
        /// <exception cref="InvalidOperationException">アセットバンドル '{info.Name}' がストレージにインストールされていないため開くことが出来ません</exception>
        public async Task<AssetBundle> OpenAsync(AssetBundleInfo info)
        {
            // このアセットバンドルが依存するアセットバンドル分回る
            foreach (var dependenceAssetBundleName in info.DependenceAssetBundleNames)
            {
                // アセットバンドル名からアセットバンドル情報を取得するが情報の取得が出来ないなら
                AssetBundleInfo dependenceAssetBundleInfo;
                if (!manifestManager.TryGetAssetBundleInfo(dependenceAssetBundleName, out dependenceAssetBundleInfo))
                {
                    // 依存解決に失敗したことを例外で吐く
                    throw new InvalidOperationException($"アセットバンドル '{info.Name}' が依存する、アセットバンドル '{dependenceAssetBundleName}' の情報が見つかりませんでした");
                }


                // 依存するアセットバンドルを再帰的に開くが開いたアセットバンドルの参照はそのまま
                await OpenAsync(dependenceAssetBundleInfo);
            }
            
            // アセットバンドル情報から名前を取得して既にコンテキストが存在するなら
            Task<AssetBundleManagementContext> context;
            if (assetBundleTable.TryGetValue(info.Name, out context))
            {
                // コンテキストからアセットバンドルを取得して返す
                return (await context).GetAssetBundle();
            }


            // 開こうとしているアセットバンドルがまだストレージに存在しないなら
            if (!storageController.Exists(info))
            {
                // 未インストールアセットバンドルが存在することを例外で吐く
                throw new InvalidOperationException($"アセットバンドル '{info.Name}' がストレージにインストールされていないため開くことが出来ません");
            }

            // 新しくコントローラからアセットバンドルを開くことを要求する
            var createContextTask = CreateAssetBundleManagementContextAsync(info);
            assetBundleTable[info.Name] = createContextTask;
            return (await createContextTask).GetAssetBundle();
        }

        public async Task<AssetBundle> OpenAsync(ImtCatalog catalog, ImtCatalogItem item)
        {
            foreach (var dependenceAssetBundleName in item.DependentItemNames)
            {
                var dependentItem = catalog.GetItem(dependenceAssetBundleName);
                if (dependentItem == null)
                {
                    throw new InvalidOperationException($"アセットバンドル '{item.Name}' が依存する、アセットバンドル '{dependenceAssetBundleName}' の情報が見つかりませんでした");
                }

                await OpenAsync(catalog, dependentItem);
            }

            // アセットバンドル情報から名前を取得して既にコンテキストが存在するなら
            Task<AssetBundleManagementContext> context;
            if (assetBundleTable.TryGetValue(item.Name, out context))
            {
                // コンテキストからアセットバンドルを取得して返す
                return (await context).GetAssetBundle();
            }


            // 開こうとしているアセットバンドルがまだストレージに存在しないなら
            if (!storage.ExistsAsset(item.LocalUri))
            {
                // 未インストールアセットバンドルが存在することを例外で吐く
                throw new InvalidOperationException($"アセットバンドル '{item.Name}' がストレージにインストールされていないため開くことが出来ません");
            }

            // 新しくコントローラからアセットバンドルを開くことを要求する
            var createContextTask = CreateAssetBundleManagementContextAsync(item);
            assetBundleTable[item.Name] = createContextTask;
            return (await createContextTask).GetAssetBundle();
        }

        private async Task<AssetBundleManagementContext> CreateAssetBundleManagementContextAsync(AssetBundleInfo info)
        {
            var assetBundle = await storageController.OpenAsync(info);
            return new AssetBundleManagementContext(assetBundle);
        }

        private async Task<AssetBundleManagementContext> CreateAssetBundleManagementContextAsync(ImtCatalogItem item)
        {
            //var stream = storage.OpenAsset(item.LocalUri, AssetStorageAccess.Read);
            // 指定されたパスのアセットバンドルを非同期で開くが開けなかったら
            var assetBundle = await AssetBundle.LoadFromFileAsync(storage.ToAssetFilePath(item.LocalUri));
            if (assetBundle == null)
            {
                // アセットバンドルが開けなかったことを例外で吐く
                throw new InvalidOperationException($"アセットバンドル '{item.Name}' を開くことが出来ませんでした");
            }

            // 開いたアセットバンドルを返す
            return new AssetBundleManagementContext(assetBundle);
        }

        /// <summary>
        /// 指定されたアセットバンドルの利用を破棄します。
        /// また、アセットバンドルの依存解決も同時に行い、依存するアセットバンドルも破棄します。
        /// </summary>
        /// <param name="assetBundle">利用を破棄するアセットバンドル</param>
        public void Release(AssetBundle assetBundle)
        {
            // アセットバンドル管理テーブルを回る
            var assetBundleInfo = default(AssetBundleInfo);
            foreach (var managementRecord in assetBundleTable)
            {
                // そもそもLoadが終わってない場合や参照が違うアセットバンドルなら
                var task = managementRecord.Value;
                if (!task.IsCompleted || !task.Result.EqualAssetBundleReference(assetBundle))
                {
                    // 次のレコードへ
                    continue;
                }


                // 管理名からアセットバンドル情報を取得してループから抜ける
                manifestManager.GetAssetBundleInfo(managementRecord.Key, out assetBundleInfo);
                break;
            }


            // この時点でアセットバンドル情報が存在していないなら
            if (string.IsNullOrWhiteSpace(assetBundleInfo.Name))
            {
                // そもそも解放すべきアセットバンドルが存在しないか解放済みのため終了
                return;
            }


            // アセットバンドル情報から依存するアセットバンドル分回る
            foreach (var dependenceAssetBundleName in assetBundleInfo.DependenceAssetBundleNames)
            {
                // 依存先アセットバンドル名からアセットバンドル管理コンテキストを取得するが、管理テーブルに無いなら
                var dependenceContextTask = default(Task<AssetBundleManagementContext>);
                if (!assetBundleTable.TryGetValue(dependenceAssetBundleName, out dependenceContextTask))
                {
                    // 次の依存アセットバンドルへ
                    continue;
                }

                //まだLoad中のAssetBundle
                if (!dependenceContextTask.IsCompleted)
                {
                    //memo:<e.kudaka>正直スキップしていいのかはわからない
                    //Release候補においてもいいかもしれない
                    continue;
                }

                // 依存先アセットバンドルの参照を取り出す
                var dependenceAssetBundle = dependenceContextTask.Result.GetAssetBundle();
                dependenceContextTask.Result.ReleaseAssetBundle(dependenceAssetBundle);


                // 再帰的に解放を行う
                Release(dependenceAssetBundle);
            }


            // 自身のアセットバンドル管理コンテキストを取得して存在しないなら
            var contextTask = default(Task<AssetBundleManagementContext>);
            if (!assetBundleTable.TryGetValue(assetBundleInfo.Name, out contextTask))
            {
                // 何もせず終了
                return;
            }

            if (!contextTask.IsCompleted)
                return;

            // コンテキストから解放を行い参照が無くなった場合は
            contextTask.Result.ReleaseAssetBundle(assetBundle);
            if (contextTask.Result.Unreferenced)
            {
                // アセットストレージコントローラに閉じるように要求して、管理テーブルからレコードを削除する
                storageController.Close(assetBundle);
                assetBundleTable.Remove(assetBundleInfo.Name);
            }
        }
    }
}