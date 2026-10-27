class Lunate < Formula
  desc "A native C# coding agent for the terminal"
  homepage "https://github.com/wdaniel1993/lunate"
  version "0.1.0-rc.2"
  license "MIT"

  on_macos do
    url "https://github.com/wdaniel1993/lunate/releases/download/v0.1.0-rc.2/lunate-osx-arm64.tar.gz"
    sha256 "15fe48cff11cc2b299507df85892a5cb4e76e78316406334fdebfe2085bc1f50"
  end

  on_linux do
    url "https://github.com/wdaniel1993/lunate/releases/download/v0.1.0-rc.2/lunate-linux-x64.tar.gz"
    sha256 "757543835439957a58bcc94aabc66bafc1a073c4f5ebd56659d4e0dd049adce2"
  end

  def install
    bin.install "lunate"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/lunate --version")
  end
end
