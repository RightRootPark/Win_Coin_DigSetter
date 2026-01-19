:: XMRig 실행 (Hint 성능 옵션)
D:\xmrig-6.24.0\xmrig.exe ^
-a rx ^
-o stratum+ssl://rx-asia.unmineable.com:443 ^
-u ALGO:Y3NPRE7TTC4G2HNKCOTMH5YRVB2OXOWLA4GJYWNGHTNKD7FYMJJL7MJSNA.unmineable_worker_dft_dt_cpu  ^
-p x ^
--cpu-priority 0 ^
--cpu-max-threads-hint=77 ^
::--opencl ^
::--cuda ^
--hube-pages-jit ^
--randomx-mode=fast ^
--randomx-wrmsr ^
--randomx-rdmsr ^
--print-time=60 ^
--keepalive=true ^
--log-file=Loging.txt

pause