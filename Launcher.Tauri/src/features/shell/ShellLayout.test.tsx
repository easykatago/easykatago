import "@testing-library/jest-dom";
import { render, screen } from "@testing-library/react";
import { ShellLayout } from "./ShellLayout";

it("renders primary navigation entries", () => {
  render(<ShellLayout />);
  expect(screen.getByText("首页")).toBeInTheDocument();
  expect(screen.getByText("安装向导")).toBeInTheDocument();
  expect(screen.getByText("权重管理")).toBeInTheDocument();
});
