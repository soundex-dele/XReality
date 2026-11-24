using System;
using System.Collections;
using System.IO;
using System.Globalization;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.Networking;

namespace XReality
{
    internal sealed class FaceModel : MonoBehaviour
    {
        internal const string FirstUse = "首次使用需下载约 3.8 MB 的面捕模型。";
        internal const string FileName = "face_landmarker.task";
        internal const string DownloadUrl = "https://storage.googleapis.com/mediapipe-models/face_landmarker/face_landmarker/float16/1/face_landmarker.task";
        internal const string Sha256 = "64184e229b263107bc2b804c6625db1341ff2bb731874b0bcc2fe6544e0bc9ff";
        internal const int Bytes = 3758596;
        internal static string QaCacheFolder;
        internal static string CacheFolder => QaCacheFolder ?? Path.Combine(Application.persistentDataPath, "Models");
        internal static string CachePath => Path.Combine(CacheFolder, FileName);

        internal const string Retrying = "网络中断，正在自动重试（2/2）…";
        internal const string RetryExhausted = "网络不稳定，重试后仍未完成。可用浏览器下载。";
        internal const string Resuming = "网络中断，正在续传（2/2）…";
        const int Attempts = 2;
        int qaTransientFailures;
        bool qaInterrupt;
        internal bool QaResumeUsed { get; private set; }

        internal void ArmQaResume()
        {
            if (!Application.isBatchMode || QaCacheFolder == null || Busy)
                throw new InvalidOperationException("An interruption can only be armed in idle, isolated batch QA.");
            qaInterrupt = true;
            QaResumeUsed = false;
        }
        internal int QaAttempts { get; private set; }

        internal void SetQaTransientFailures(int count)
        {
            if (!Application.isBatchMode || QaCacheFolder == null || Busy || count < 0 || count > Attempts)
                throw new InvalidOperationException("Download faults are restricted to idle, isolated batch QA.");
            qaTransientFailures = count;
            QaAttempts = 0;
        }

        internal const int QaDownloadDeadlineSeconds = Attempts * ModelTransferBudget.AttemptSeconds + 10;
        UnityWebRequest request;
        Coroutine operation;
        string temporary, prefix;
        long resumeOffset;
        internal bool Ready { get; private set; }
        internal bool Busy => operation != null;
        internal string Status { get; private set; }
        internal float Progress => Mathf.Clamp01((float)(request == null ? (ulong)resumeOffset :
            request.downloadedBytes + (request.responseCode == 200 ? 0 : (ulong)resumeOffset)) / Bytes);

        internal void Refresh()
        {
            if (Busy) return;
            try
            {
                ReadVerified(CachePath);
                Ready = true;
                Status = "面捕模型已就绪 · 可离线使用";
            }
            catch (FileNotFoundException)
            {
                Ready = false;
                Status = FirstUse;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidDataException)
            {
                Ready = false;
                Status = "无法读取或校验模型，请重新下载。";
            }
        }

        internal static byte[] ReadVerified(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Face Landmarker model not installed.", path);
            if (new FileInfo(path).Length != Bytes) throw new InvalidDataException("Face Landmarker model size mismatch.");
            var bytes = File.ReadAllBytes(path);
            using var hash = SHA256.Create();
            if (!string.Equals(BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", ""), Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Face Landmarker model checksum mismatch.");
            return bytes;
        }

        internal void BeginDownload()
        {
            if (Busy) return;
            Status = "正在从 Google 下载模型…";
            operation = StartCoroutine(Receive());
        }

        bool OpenRequest(out UnityWebRequestAsyncOperation download)
        {
            download = null;
            try
            {
                Directory.CreateDirectory(CacheFolder);
                temporary = Path.Combine(CacheFolder, Guid.NewGuid().ToString("N") + ".partial");
                request = new UnityWebRequest(DownloadUrl, UnityWebRequest.kHttpVerbGET)
                {
                    timeout = ModelTransferBudget.AttemptSeconds,
                    redirectLimit = 0,
                    downloadHandler = new DownloadHandlerFile(temporary) { removeFileOnAbort = false }
                };
                if (resumeOffset > 0) request.SetRequestHeader("Range", "bytes=" + Number(resumeOffset) + "-");
                download = request.SendWebRequest();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("Model download could not start: " + e.GetType().Name);
                Status = "无法开始下载，请检查网络或模型目录权限。";
                return false;
            }
        }

        IEnumerator Receive()
        {
            // Assign the Coroutine handle before reaching any path that clears it.
            yield return null;
            try
            {
                for (int attempt = 1; attempt <= Attempts; attempt++)
                {
                    bool tooLarge = false, timedOut = false, interrupted = false;
                    long http = 0;
                    ulong received = 0;
                    string range = null;
                    UnityWebRequest.Result result;
                    if (qaTransientFailures > 0)
                    {
                        qaTransientFailures--;
                        result = UnityWebRequest.Result.ConnectionError;
                        Debug.Log("XR_QA_DOWNLOAD_FAULT: injected connection error");
                    }
                    else
                    {
                        if (!OpenRequest(out var download)) yield break;
                        var budget = new ModelTransferBudget(Time.realtimeSinceStartupAsDouble);
                        while (!download.isDone && !Oversize(request.responseCode, request.downloadedBytes))
                        {
                            if (qaInterrupt && attempt == 1 && request.responseCode == 200 &&
                                request.downloadedBytes >= 384 * 1024 && request.downloadedBytes < Bytes)
                            {
                                qaInterrupt = false;
                                interrupted = true;
                                Debug.Log("XR_QA_DOWNLOAD_FAULT: interrupted a live partial response");
                                break;
                            }
                            if (budget.Expired(Time.realtimeSinceStartupAsDouble, request.downloadedBytes))
                            {
                                timedOut = true;
                                break;
                            }
                            yield return null;
                        }
                        http = request.responseCode;
                        received = request.downloadedBytes;
                        tooLarge = Oversize(http, received);
                        range = request.GetResponseHeader("Content-Range");
                        result = timedOut || interrupted ? UnityWebRequest.Result.ConnectionError : request.result;
                        bool success = !timedOut && !interrupted && !tooLarge && download.isDone && result == UnityWebRequest.Result.Success;
                        DisposeRequest(!success);
                        if (success)
                        {
                            if (http == 200 || (http == 206 && resumeOffset > 0 && IsExactRange(range, resumeOffset)))
                                InstallVerified(http == 206);
                            else Status = "服务器返回的文件范围异常，未安装。可用浏览器下载。";
                            yield break;
                        }
                    }
                    if (QaCacheFolder != null && Application.isBatchMode) QaAttempts++;

                    Debug.LogWarning($"Model download attempt {attempt}/{Attempts}: {result}; HTTP {http}; bytes {received}; offset {resumeOffset}; progress budget expired {timedOut}; oversize {tooLarge}");
                    bool transient = result == UnityWebRequest.Result.ConnectionError ||
                        http == 408 || http == 429 || (http >= 500 && http <= 599);
                    if (tooLarge || !transient || attempt == Attempts)
                    {
                        Status = tooLarge ? "下载文件大小异常，未安装。" :
                            transient ? RetryExhausted : "下载未完成，可用浏览器下载或手动安装。";
                        yield break;
                    }
                    if (http == 200) RetainPrefix();
                    ClearAttempt();
                    Status = resumeOffset > 0 ? Resuming : Retrying;
                    yield return new WaitForSecondsRealtime(1.2f);
                }
            }
            finally
            {
                ClearTransfer();
                operation = null;
            }
        }

        bool Oversize(long status, ulong bytes) => bytes > (ulong)(Bytes - (status == 206 ? resumeOffset : 0));

        static string Number(long n) => n.ToString(CultureInfo.InvariantCulture);

        internal static bool IsExactRange(string header, long offset) => offset > 0 && offset < Bytes &&
            string.Equals(header, "bytes " + Number(offset) + "-" + Number(Bytes - 1) + "/" + Number(Bytes), StringComparison.Ordinal);

        void RetainPrefix()
        {
            try
            {
                long length = new FileInfo(temporary).Length;
                if (length <= 0 || length >= Bytes) return;
                prefix = temporary;
                temporary = null;
                resumeOffset = length;
                Debug.Log($"Model download retained {length} bytes for the next attempt");
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogWarning("Model download could not retain a partial file: " + e.GetType().Name);
            }
        }

        internal static void JoinSuffix(string prefixPath, string chunkPath, long offset)
        {
            using var prefixData = new FileStream(prefixPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            using var chunk = new FileStream(chunkPath, FileMode.Open, FileAccess.Read, FileShare.None);
            if (offset <= 0 || offset >= Bytes || prefixData.Length != offset || chunk.Length != Bytes - offset)
                throw new InvalidDataException("Model download segment lengths do not match the requested range.");
            prefixData.Position = offset;
            chunk.CopyTo(prefixData);
        }

        void InstallVerified(bool ranged)
        {
            try
            {
                string completed = temporary;
                if (ranged)
                {
                    JoinSuffix(prefix, temporary, resumeOffset);
                    completed = prefix;
                }
                ReadVerified(completed);
                if (File.Exists(CachePath)) File.Replace(completed, CachePath, null);
                else File.Move(completed, CachePath);
                Debug.Log(ranged ? $"XR_MODEL_DOWNLOAD_RESUMED: {resumeOffset} bytes reused" : "XR_MODEL_DOWNLOAD_FULL: checksum verified");
                if (QaCacheFolder != null && Application.isBatchMode) QaResumeUsed = ranged;
                Ready = true;
                Status = "下载并校验成功 · 可离线使用";
            }
            catch (Exception e)
            {
                Debug.LogWarning("Model download was not installed: " + e.GetType().Name);
                Status = "模型校验或保存失败，未安装此文件。";
            }
        }

        internal void Cancel()
        {
            if (operation == null) return;
            StopCoroutine(operation);
            operation = null;
            ClearTransfer();
            Status = "已取消下载，可随时重试。";
        }

        void DisposeRequest(bool abort)
        {
            if (request == null) return;
            if (abort) request.Abort();
            request.Dispose();
            request = null;
        }

        static void RemovePartial(ref string path)
        {
            if (path == null) return;
            try { File.Delete(path); }
            catch (Exception e) { Debug.LogWarning("Could not remove partial model: " + e.GetType().Name); }
            path = null;
        }

        void ClearAttempt()
        {
            DisposeRequest(true);
            RemovePartial(ref temporary);
        }

        void ClearTransfer()
        {
            ClearAttempt();
            RemovePartial(ref prefix);
            resumeOffset = 0;
            qaInterrupt = false;
        }
        void OnDisable() => Cancel();
    }
}
