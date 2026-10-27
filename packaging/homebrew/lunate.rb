class Lunate < Formula
  desc "Native C# coding agent for the terminal"
  homepage "https://github.com/wdaniel1993/lunate"
  license "MIT"

  on_macos do
    depends_on arch: :arm64
  end

  on_linux do
    depends_on arch: :x86_64
  end

  if OS.mac?
    url "https://github.com/wdaniel1993/lunate/releases/download/v0.1.0/lunate-osx-arm64.tar.gz"
    sha256 "2eb731ba7c99632a8388459e5c2d921541b5cec5b2607fb57f2926c37a3d4612"
  elsif OS.linux?
    url "https://github.com/wdaniel1993/lunate/releases/download/v0.1.0/lunate-linux-x64.tar.gz"
    sha256 "6263a19d71a2078274b6b701833450b7e8c81adcacf329bcbbf481f0567a0d05"
  end

  def install
    bin.install "lunate"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/lunate --version")
  end
end
