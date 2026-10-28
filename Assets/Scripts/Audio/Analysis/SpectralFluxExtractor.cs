using System;

// 음원 샘플에서 주파수별 증가량을 계산한다.
// 파일 로드, 음악 재생, BPM 후보 선택은 담당하지 않는다.
public sealed class SpectralFluxExtractor
{
    // 주파수별 작은 성분도 비교할 수 있도록 사용하는 로그 계수.
    // 무조건 크게 하면 작은 잡음까지 강조될 수 있다.
    private const double LogCompression = 100.0;

    private readonly int size;
    private readonly int channels;
    private readonly int binCount;

    // Hann 창과 FFT 계산 공간.
    // 창마다 배열을 새로 만들지 않고 재사용한다.
    private readonly double[] window; // 잘라 읽은 구간의 경계를 부드럽게 만든기 위한 변수
    private readonly double[] real; // 주파수 변환 계산 공간
    private readonly double[] imaginary; // 주파수 변환 계산 공간

    // FFT에서 반복 사용하는 회전 계수를 미리 계산한다.
    private readonly double[] cosine;
    private readonly double[] sine;

    // FFT 계산에 필요한 비트 역순 인덱스.
    private readonly int[] reversedIndices;

    // 채널별 이전 주파수 크기를 저장한다.
    private readonly double[] previousLogSpectrum; // 직전 구간과 비교할 기록

    private readonly double magnitudeScale;

    private bool hasPreviousWindow;

    public SpectralFluxExtractor(int fftSize, int channelCount)
    {
        // 아래 FFT는 길이가 2의 거듭제곱이어야 한다.
        // 예: 512, 1024, 2048, 4096.
        if (fftSize < 4 ||
            (fftSize & (fftSize - 1)) != 0 ||
            channelCount < 1)
        {
            throw new ArgumentException(
                "FFT 크기는 4 이상의 2의 거듭제곱이며, " +
                "채널 수는 1 이상이어야 합니다.");
        }

        size = fftSize;
        channels = channelCount;
        binCount = size / 2;

        window = new double[size];
        real = new double[size];
        imaginary = new double[size];

        cosine = new double[size / 2];
        sine = new double[size / 2];

        reversedIndices = new int[size];
        previousLogSpectrum =
            new double[channels * binCount];

        double windowSum = 0.0;

        for (int i = 0; i < size; i++)
        {
            // Hann 창:
            // w(i) = 0.5 - 0.5 × cos(2πi / (N - 1))
            //
            // 잘라 읽은 구간의 양 끝을 부드럽게 줄여
            // 구간 경계 때문에 생기는 주파수 번짐을 완화한다.
            double weight =
                0.5 -
                0.5 * Math.Cos(
                    2.0 * Math.PI * i / (size - 1));

            window[i] = weight;
            windowSum += weight;
        }

        // FFT 크기와 창 함수에 따른 크기 차이를 보정한다.
        // 양의 주파수 성분을 사용하므로 2를 곱한다.
        magnitudeScale = 2.0 / windowSum;

        for (int i = 0; i < size / 2; i++)
        {
            double angle = -2.0 * Math.PI * i / size;

            cosine[i] = Math.Cos(angle);
            sine[i] = Math.Sin(angle);
        }

        int bitCount = 0;

        for (int value = size; value > 1; value >>= 1)
        {
            bitCount++;
        }

        for (int i = 0; i < size; i++)
        {
            int source = i;
            int reversed = 0;

            // 예: 3비트에서 001 → 100.
            // FFT가 작은 계산 결과를 단계별로 합칠 수 있도록
            // 입력 순서를 미리 정한다.
            for (int bit = 0; bit < bitCount; bit++)
            {
                reversed =
                    (reversed << 1) | (source & 1);

                source >>= 1;
            }

            reversedIndices[i] = reversed;
        }
    }

    // 현재 구간에서 새롭게 강해진 소리의 정도를 반환한다.
    public double GetFlux(float[] interleavedSamples)
    {
        if (interleavedSamples == null ||
            interleavedSamples.Length != size * channels)
        {
            throw new ArgumentException(
                "샘플 배열 길이는 FFT 크기 × 채널 수여야 합니다.");
        }

        double totalRise = 0.0;

        // 채널을 먼저 평균내면 반대 위상의 소리가 상쇄될 수 있다.
        // 따라서 채널별 FFT를 계산하고 변화량을 합친다.
        for (int channel = 0; channel < channels; channel++)
        {
            for (int i = 0; i < size; i++)
            {
                // 스테레오 샘플은 L, R, L, R 순서로 저장된다.
                int sampleIndex = i * channels + channel;
                int destination = reversedIndices[i];

                real[destination] =
                    interleavedSamples[sampleIndex] * window[i];

                imaginary[destination] = 0.0;
            }

            Transform();

            int spectrumOffset = channel * binCount;

            // 0번은 DC 성분이므로 제외한다.
            // 음수 주파수 쪽은 중복 정보이므로 사용하지 않는다.
            // 마지막 Nyquist 성분도 이번 계산에서는 제외한다.
            for (int bin = 1; bin < binCount; bin++)
            {
                // 복소수 크기 = sqrt(실수부² + 허수부²).
                double magnitude = Math.Sqrt(
                    real[bin] * real[bin] +
                    imaginary[bin] * imaginary[bin]);

                magnitude *= magnitudeScale;

                // 큰 성분의 영향이 지나치게 커지지 않도록 압축한다.
                // log(1 + 계수 × 크기).
                double logMagnitude =
                    Math.Log(
                        1.0 + LogCompression * magnitude);

                int index = spectrumOffset + bin;

                double rise =
                    logMagnitude - previousLogSpectrum[index];

                // 줄어든 소리보다 새로 강해진 소리에 주목한다.
                if (rise > 0.0)
                {
                    totalRise += rise;
                }

                previousLogSpectrum[index] = logMagnitude;
            }
        }

        // 첫 창에는 비교할 이전 창이 없다.
        // 첫 창 전체를 갑작스러운 소리로 오인하지 않도록 0을 반환한다.
        double result = hasPreviousWindow
            ? totalRise / (channels * (binCount - 1))
            : 0.0;

        hasPreviousWindow = true;
        return result;
    }

    // 샘플을 주파수 정보로 변환한다.
    private void Transform()
    {
        // 2개씩 계산한 결과를 4개, 8개, 16개...로 합친다.
        // 일반적인 직접 계산 O(N²) 대신 O(N log N)으로 처리한다.
        for (int blockSize = 2;
             blockSize <= size;
             blockSize <<= 1)
        {
            int halfSize = blockSize / 2;
            int rotationStep = size / blockSize;

            for (int start = 0; start < size; start += blockSize)
            {
                for (int offset = 0; offset < halfSize; offset++)
                {
                    int first = start + offset;
                    int second = first + halfSize;
                    int rotation = offset * rotationStep;

                    // 두 번째 값을 해당 주파수의 회전 계수와 곱한다.
                    double rotatedReal =
                        cosine[rotation] * real[second] -
                        sine[rotation] * imaginary[second];

                    double rotatedImaginary =
                        sine[rotation] * real[second] +
                        cosine[rotation] * imaginary[second];

                    double firstReal = real[first];
                    double firstImaginary = imaginary[first];

                    // 두 부분의 합과 차를 저장하는 버터플라이 계산.
                    real[first] = firstReal + rotatedReal;
                    imaginary[first] =
                        firstImaginary + rotatedImaginary;

                    real[second] = firstReal - rotatedReal;
                    imaginary[second] =
                        firstImaginary - rotatedImaginary;
                }
            }
        }
    }
}