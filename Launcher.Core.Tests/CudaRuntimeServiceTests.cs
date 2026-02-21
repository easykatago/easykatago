using Launcher.Core.Services;

namespace Launcher.Core.Tests;

public sealed class CudaRuntimeServiceTests
{
    [Fact]
    public void IsCudaBackend_WhenBackendIsCuda_ReturnsTrue()
    {
        Assert.True(CudaRuntimeService.IsCudaBackend("cuda"));
    }

    [Fact]
    public void Probe_WhenCublasAndCudnnExistInDifferentFolders_ReturnsReady()
    {
        var root = Path.Combine(Path.GetTempPath(), "easykatago-cuda-probe-" + Guid.NewGuid().ToString("N"));
        var cudaBin = Path.Combine(root, "cuda", "v12.8", "bin");
        var cudnnBin = Path.Combine(root, "cudnn", "v9.8", "bin", "12.8", "x64");
        var katagoPath = Path.Combine(root, "components", "katago", "1.16.4", "cuda", "katago.exe");

        try
        {
            Directory.CreateDirectory(cudaBin);
            Directory.CreateDirectory(cudnnBin);
            Directory.CreateDirectory(Path.GetDirectoryName(katagoPath)!);
            File.WriteAllText(Path.Combine(cudaBin, "cublas64_12.dll"), string.Empty);
            File.WriteAllText(Path.Combine(cudnnBin, "cudnn64_9.dll"), string.Empty);

            var probe = CudaRuntimeService.Probe(katagoPath, root);

            Assert.True(probe.IsReady);
            Assert.NotNull(probe.CudaDirectory);
            Assert.NotNull(probe.CudnnDirectory);
            Assert.True(CudaRuntimeService.LooksReadyForCuda(katagoPath, probe.RuntimeDirectories));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
