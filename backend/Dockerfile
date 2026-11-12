# Multi-stage — SDK দিয়ে build/publish, ছোট runtime image দিয়ে চালানো।
# final image-এ কোনো .cs ফাইল বা SDK নেই, শুধু compiled DLL।
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# ponytail: csproj আগে copy করে restore করার layer-cache কৌশলটা বাদ দিলাম —
# ওটা থাকলে পরের publish-এ CS5001 ("no static Main") হয়, কারণ .cs ফাইল ছাড়া করা
# restore-এর obj/ state-এ Api project-এর entry point আর খুঁজে পাওয়া যায় না।
# দাম: dependency না বদলালেও restore আবার চলে (~৩০s)। correctness > cache।
COPY src/ src/
RUN dotnet publish src/Api/AiPlatform.Api.csproj -c Release -o /app/publish

# .pdb বাদ — PDB-তে SourceLink থাকে, যা image থেকে সোজা GitHub repo + commit sha ফাঁস
# করে (হুবহু source নামানো যায়)। লাইন নম্বর ও local variable-এর নামও PDB-তেই, তাই
# থাকলে decompile অনেক বেশি পড়ার যোগ্য হয়।
# ⚠️ IL তবু decompile হয় (ILSpy) — এটা সেটা ঠেকায় না, শুধু সহজ পথটা বন্ধ করে।
# ponytail: publish-এর পর মুছি। -p:DebugType=none দিলেও একই CS5001 হয়, তাই এই পথ।
#
# dev config-ও বাদ: Production-এ লোড হয় না, কিন্তু ভেতরের dev JWT key ও SQL password পড়া যায়।
RUN rm -f /app/publish/*.pdb /app/publish/appsettings.Development.json

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "AiPlatform.Api.dll"]
