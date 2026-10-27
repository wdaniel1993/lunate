class Lunate < Formula
  desc "A native C# coding agent for the terminal"
  homepage "https://github.com/wdaniel1993/lunate"
  version "0.1.0-rc.1"
  license "MIT"

  on_macos do
    url "https://github.com/wdaniel1993/lunate/releases/download/v0.1.0-rc.1/lunate-osx-arm64.tar.gz"
    sha256 "14feb76b814b3c51a3ab55c7cde54d573207a8eb120e8a61276ad069ee5f17e6"
  end

  on_linux do
    url "https://github.com/wdaniel1993/lunate/releases/download/v0.1.0-rc.1/lunate-linux-x64.tar.gz"
    sha256 "c6125487818ea2daed1f3621d033b1dfd366213c2833c04d711286e4349197cf"
  end

  def install
    bin.install "lunate"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/lunate --version")
  end
end
