using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public class AddressablesDataManager : MonoBehaviour
{
    private class HandleInfo
    {
        public AsyncOperationHandle Handle;
        public int RefCount;
    }

    private static readonly Dictionary<string, HandleInfo> _addressableHandles = new();
    private static int _releaseCount = 0;

    #region アセットのロード
    /// <summary>
    /// 指定のアドレスのアセットをロード
    /// </summary>
    /// <typeparam name="T">アセットの型</typeparam>
    /// <param name="address">アセットのアドレス</param>
    /// <returns></returns>
    /// <exception cref="Exception">ロード失敗時</exception>
    public static async Task<T> GetAssetByAddress<T>(string address)
    {
        if (_addressableHandles.TryGetValue(address, out var info))
        {//ロード済みなら参照数追加
            info.RefCount++;
            Log($"Load => {address} (ref={info.RefCount})");
            return (T)info.Handle.Result;
        }

        var handle = Addressables.LoadAssetAsync<T>(address);
        await handle.Task;

        if (handle.Status != AsyncOperationStatus.Succeeded)
        {//ロード失敗時
            if (handle.IsValid())
                Addressables.Release(handle);

            throw new Exception($"アセットのロードに失敗しました: {address}");
        }

        _addressableHandles[address] = new HandleInfo
        {//新規アセットのロード時
            Handle = handle,
            RefCount = 1
        };

        Log($"LoadNew => {address} (ref=1)");
        return handle.Result;
    }

    /// <summary>
    /// 指定のラベルのアセットを一括ロード
    /// </summary>
    /// <typeparam name="T">アセットの型</typeparam>
    /// <param name="label">アセットのラベル</param>
    /// <returns></returns>
    public static async Task<List<T>> GetAssetsByLabel<T>(string label)
    {
        var addresses = await GetAddressesByLabel(label);
        var results = new List<T>(addresses.Count);

        foreach (var address in addresses)
        {
            var asset = await GetAssetByAddress<T>(address);
            results.Add(asset);
        }

        Log($"LoadByLabel => {label} (count={results.Count})");
        return results;
    }
    #endregion

    #region アセットのリリース
    /// <summary>
    /// 指定のアドレスのアセットのリリース用関数
    /// </summary>
    /// <param name="address">アセットのアドレス</param>
    /// リリースするたびに指定アドレスの参照カウントを減らして参照が0になったらメモリから解放します。
    /// ただしリリース前にプロジェクト内の参照を消しておかないとメモリから解放されません。
    /// リリース後にプロジェクト内の参照を消しても一応メモリ解放はされると思います。(非推奨)
    public static void Release(string address)
    {
        if (!_addressableHandles.TryGetValue(address, out var info)) return;

        info.RefCount--;
        Log($"Release => {address} (ref={info.RefCount})");

        if (info.RefCount <= 0)
        {//参照が0になったらメモリから解放
            if (info.Handle.IsValid())
            {
                Addressables.Release(info.Handle);
                _releaseCount++;
                Log($"Released　=> {address}");
            }

            _addressableHandles.Remove(address);
        }
    }

    /// <summary>
    /// 指定のラベルのアセットを一括リリース用関数
    /// </summary>
    /// <param name="label">アセットのラベル</param>
    /// <returns></returns>
    public static async Task ReleaseByLabel(string label)
    {
        var addresses = await GetAddressesByLabel(label);

        foreach (var address in addresses)
        {
            Release(address);
        }

        Log($"ReleaseByLabel => {label}");
    }

    /// <summary>
    /// 強制全アセットリリース
    /// </summary>
    public static void ReleaseAll()
    {
        Log("ReleaseAll Assets");

        foreach (var info in _addressableHandles.Values)
        {
            if (info.Handle.IsValid()) Addressables.Release(info.Handle);             
        }

        _addressableHandles.Clear();
    }
    #endregion

    /// <summary>
    /// ラベルからアドレス一覧を取得
    /// </summary>
    /// <param name="label">アセットのラベル</param>
    /// <returns></returns>
    /// <exception cref="Exception">ラベル検索失敗時</exception>
    private static async Task<IList<string>> GetAddressesByLabel(string label)
    {
        var locationsHandle = Addressables.LoadResourceLocationsAsync(label);
        await locationsHandle.Task;

        if (locationsHandle.Status != AsyncOperationStatus.Succeeded)
        {
            Addressables.Release(locationsHandle);
            throw new Exception($"指定ラベルのアセット検索に失敗しました: {label}");
        }

        var addresses = new List<string>();
        foreach (var loc in locationsHandle.Result)
        {
            addresses.Add(loc.PrimaryKey);
        }

        Addressables.Release(locationsHandle);
        return addresses;
    }

    /// <summary>
    /// 未使用アセットをメモリから解放する関数(フェード時などに呼び出し)
    /// </summary>
    /// <returns></returns>
    public static async UniTask ReleaseMemory()
    {
        if(_releaseCount >= 10) await Resources.UnloadUnusedAssets();
    }


    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private static void Log(string message)
    {
        Debug.Log($"AddressablesDataManagerLog => {message}");
    }
}
