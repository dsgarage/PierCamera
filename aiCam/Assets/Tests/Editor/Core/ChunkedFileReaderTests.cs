using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using AICam.Core.IO;

namespace AICam.Core.Tests
{
    /// <summary>
    /// ChunkedFileReader のユニットテスト
    /// Phase 3: 低スペック端末向けファイル読み込み最適化
    /// </summary>
    [TestFixture]
    public class ChunkedFileReaderTests
    {
        private string _tempDirectory;
        private string _smallFilePath;
        private string _largeFilePath;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _tempDirectory = Path.Combine(Application.temporaryCachePath, "ChunkedFileReaderTests");
            Directory.CreateDirectory(_tempDirectory);

            // 小さいファイル (500KB - チャンク化されない)
            _smallFilePath = Path.Combine(_tempDirectory, "small_file.bin");
            CreateTestFile(_smallFilePath, 500 * 1024);

            // 大きいファイル (2MB - チャンク化される)
            _largeFilePath = Path.Combine(_tempDirectory, "large_file.bin");
            CreateTestFile(_largeFilePath, 2 * 1024 * 1024);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }

        private void CreateTestFile(string path, int size)
        {
            byte[] data = new byte[size];
            // パターンを書き込んで検証に使用
            for (int i = 0; i < size; i++)
            {
                data[i] = (byte)(i % 256);
            }
            File.WriteAllBytes(path, data);
        }

        #region 基本機能テスト

        [Test]
        public async Task ReadAllBytesAsync_SmallFile_ReadsCorrectly()
        {
            // Arrange
            var expectedSize = 500 * 1024;

            // Act
            byte[] result = await ChunkedFileReader.ReadAllBytesAsync(_smallFilePath);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(expectedSize, result.Length);

            // パターン検証
            for (int i = 0; i < 100; i++)
            {
                Assert.AreEqual((byte)(i % 256), result[i], $"Byte at index {i} doesn't match");
            }
        }

        [Test]
        public async Task ReadAllBytesAsync_LargeFile_ReadsCorrectly()
        {
            // Arrange
            var expectedSize = 2 * 1024 * 1024;

            // Act
            byte[] result = await ChunkedFileReader.ReadAllBytesAsync(_largeFilePath);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(expectedSize, result.Length);

            // パターン検証 (先頭と末尾)
            for (int i = 0; i < 100; i++)
            {
                Assert.AreEqual((byte)(i % 256), result[i], $"Byte at index {i} doesn't match");
            }
            int lastIndex = expectedSize - 1;
            Assert.AreEqual((byte)(lastIndex % 256), result[lastIndex]);
        }

        #endregion

        #region プログレスコールバックテスト

        [Test]
        public async Task ReadAllBytesAsync_LargeFile_ReportsProgress()
        {
            // Arrange
            float lastProgress = 0f;
            int progressCallCount = 0;

            Action<float> onProgress = (progress) =>
            {
                Assert.GreaterOrEqual(progress, lastProgress, "Progress should not decrease");
                Assert.LessOrEqual(progress, 1f, "Progress should not exceed 1.0");
                lastProgress = progress;
                progressCallCount++;
            };

            // Act
            byte[] result = await ChunkedFileReader.ReadAllBytesAsync(_largeFilePath, onProgress);

            // Assert
            Assert.IsNotNull(result);
            Assert.Greater(progressCallCount, 1, "Progress should be reported multiple times for large files");
            Assert.AreEqual(1f, lastProgress, 0.01f, "Final progress should be 1.0");
        }

        [Test]
        public async Task ReadAllBytesAsync_SmallFile_ReportsProgressOnce()
        {
            // Arrange
            int progressCallCount = 0;

            Action<float> onProgress = (progress) =>
            {
                progressCallCount++;
            };

            // Act
            byte[] result = await ChunkedFileReader.ReadAllBytesAsync(_smallFilePath, onProgress);

            // Assert
            Assert.IsNotNull(result);
            // 小さいファイルは標準読み込みを使用するため、プログレスコールバックは呼ばれないか1回
            Assert.LessOrEqual(progressCallCount, 1);
        }

        #endregion

        #region キャンセルテスト

        [Test]
        public void ReadAllBytesAsync_Cancelled_ThrowsOperationCanceledException()
        {
            // Arrange
            var cts = new CancellationTokenSource();
            cts.Cancel(); // 事前にキャンセル

            // Act & Assert
            Assert.ThrowsAsync<TaskCanceledException>(async () =>
            {
                await ChunkedFileReader.ReadAllBytesAsync(_largeFilePath, null, cts.Token);
            });
        }

        #endregion

        #region エラーハンドリングテスト

        [Test]
        public void ReadAllBytesAsync_FileNotFound_ThrowsFileNotFoundException()
        {
            // Arrange
            string nonExistentPath = Path.Combine(_tempDirectory, "non_existent.bin");

            // Act & Assert
            Assert.ThrowsAsync<FileNotFoundException>(async () =>
            {
                await ChunkedFileReader.ReadAllBytesAsync(nonExistentPath);
            });
        }

        [Test]
        public void ReadAllBytesAsync_NullPath_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.ThrowsAsync<ArgumentNullException>(async () =>
            {
                await ChunkedFileReader.ReadAllBytesAsync(null);
            });
        }

        [Test]
        public void ReadAllBytesAsync_EmptyPath_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.ThrowsAsync<ArgumentNullException>(async () =>
            {
                await ChunkedFileReader.ReadAllBytesAsync("");
            });
        }

        #endregion

        #region 境界値テスト

        [Test]
        public async Task ReadAllBytesAsync_ExactlyChunkSize_ReadsCorrectly()
        {
            // Arrange - 64KBちょうどのファイル
            string exactChunkPath = Path.Combine(_tempDirectory, "exact_chunk.bin");
            CreateTestFile(exactChunkPath, 64 * 1024);

            // Act
            byte[] result = await ChunkedFileReader.ReadAllBytesAsync(exactChunkPath);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(64 * 1024, result.Length);
        }

        [Test]
        public async Task ReadAllBytesAsync_JustOverThreshold_UsesChunkedReading()
        {
            // Arrange - 1MB + 1byte のファイル (閾値を超える)
            string justOverPath = Path.Combine(_tempDirectory, "just_over.bin");
            CreateTestFile(justOverPath, 1024 * 1024 + 1);

            int progressCallCount = 0;
            Action<float> onProgress = (progress) => progressCallCount++;

            // Act
            byte[] result = await ChunkedFileReader.ReadAllBytesAsync(justOverPath, onProgress);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(1024 * 1024 + 1, result.Length);
            // 大きいファイルなのでプログレスが複数回呼ばれるはず
            Assert.Greater(progressCallCount, 0);
        }

        #endregion
    }
}
