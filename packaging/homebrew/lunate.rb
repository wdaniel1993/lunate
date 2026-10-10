class Lunate < Formula
  desc "A native C# coding agent for the terminal"
  homepage "https://github.com/wdaniel1993/lunate"
  version "0.1.0"
  license "MIT"

  on_macos do
    url "https://github.com/wdaniel1993/lunate/releases/download/v0.1.0/lunate-osx-arm64.tar.gz"
    sha256 "SHA256_PLACEHOLDER_OSX_ARM64"
  end

  on_linux do
    url "https://github.com/wdaniel1993/lunate/releases/download/v0.1.0/lunate-linux-x64.tar.gz"
    sha256 "SHA256_PLACEHOLDER_LINUX_X64"
  end

  def install
    bin.install "lunate"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/lunate --version")
  end
end
