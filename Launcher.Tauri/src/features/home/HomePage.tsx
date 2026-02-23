import { useState } from "react";
import { launchRun } from "../../lib/bridge";

export function HomePage() {
  const [status, setStatus] = useState("待机");

  const handleQuickLaunch = async () => {
    setStatus("启动中...");
    try {
      const result = await launchRun();
      setStatus(`启动状态: ${result.status}`);
    } catch (error) {
      const message = error instanceof Error ? error.message : "未知错误";
      setStatus(`启动失败: ${message}`);
    }
  };

  return (
    <section>
      <h2>首页</h2>
      <p>用于快速执行核心流程并查看实时状态。</p>
      <button type="button" onClick={() => void handleQuickLaunch()}>
        快速启动
      </button>
      <p>{status}</p>
    </section>
  );
}
