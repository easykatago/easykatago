import { useState } from "react";
import { installRun } from "../../lib/bridge";

export function InstallPage() {
  const [status, setStatus] = useState("未开始");

  const handleInstall = async () => {
    setStatus("安装执行中...");
    try {
      const result = await installRun();
      setStatus(`安装状态: ${result.status}`);
    } catch (error) {
      const message = error instanceof Error ? error.message : "未知错误";
      setStatus(`安装失败: ${message}`);
    }
  };

  return (
    <section>
      <h2>安装向导</h2>
      <p>触发桥接安装流程，并回显执行结果。</p>
      <button type="button" onClick={() => void handleInstall()}>
        开始安装
      </button>
      <p>{status}</p>
    </section>
  );
}
