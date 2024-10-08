pipeline {
	agent none
	stages {
		stage('Initialisation') {
			agent { node { label 'windows-node-3' }}
			steps {
				dir('') {
					script {
						env.version_short = (bat(script: "@echo off && gitversion /showvariable MajorMinorPatch", returnStdout: true)).trim()
						env.version = (bat(script: '@echo off && gitversion /showvariable FullSemVer', returnStdout: true)).trim()
						env.AssemblySemFileVer = (bat(script: '@echo off && gitversion /showvariable AssemblySemFileVer', returnStdout: true)).trim()
						env.AssemblySemVer = (bat(script: '@echo off && gitversion /showvariable AssemblySemVer', returnStdout: true)).trim()

						env.git_branch_ugcs_video_transmitter = "${GIT_BRANCH}"
						env.git_commit_ugcs_video_transmitter = "${GIT_COMMIT}"
					}
				}
			}
		}
		stage('Preparation windows-node-3') {
			agent { node { label 'windows-node-3' }}
			steps {
				dir('') {
					sh 'rm -rf build && mkdir build'
					sh '''echo "version=${version}
git_branch_ugcs_video_transmitter=${git_branch_ugcs_video_transmitter}
git_commit_ugcs_video_transmitter=${git_commit_ugcs_video_transmitter} " > build/version.ini '''

					cifsPublisher publishers: [[
						configName: 'binaries_repo',
						verbose: true,
						transfers: [
							[
								cleanRemote: true, excludes: '', 
								remoteDirectory: "ugcs-video-transmitter/${version_short}/${version}", remoteDirectorySDF: false,
								removePrefix: 'build',
								sourceFiles: 'build/version.ini'
							], [
								cleanRemote: true, excludes: '', 
								remoteDirectory: "ugcs-video-transmitter/${version_short}/latest", remoteDirectorySDF: false, 
								removePrefix: 'build',
								sourceFiles: 'build/version.ini'
							]]
					]]
				}
			}
		}
		stage('Building & Publishing: Win library') {
			agent { node { label 'windows-node-3' }}
			steps {
				bat ''' 
					mkdir build
					cd src/VideoTransmitter
					"%DOTNET_PATH_15%/msbuild.exe" /t:restore;build /p:Configuration=Release;Version="%version%";FileVersion="%AssemblySemFileVer%";AssemblyVersion="%AssemblySemVer%"
					if ERRORLEVEL 1 exit 1
					"%ARCHIVATOR_7z_PATH%/7z.exe" a -tzip %WORKSPACE%/build/ugcs-video-transmitter-win.zip %WORKSPACE%/src/VideoTransmitter/VideoTransmitter/bin/x64/Release/net4.7.2/* 
					if ERRORLEVEL 1 exit 1 '''

				cifsPublisher publishers: [[
					configName: 'binaries_repo',
					verbose: true,
					transfers: [
						[
							cleanRemote: false, excludes: '', 
							remoteDirectory: "ugcs-video-transmitter/${version_short}/${version}",
							removePrefix: 'build',
							sourceFiles: 'build/*.zip'
						], [
							cleanRemote: false, excludes: '', 
							remoteDirectory: "ugcs-video-transmitter/${version_short}/latest",
							removePrefix: 'build',
							sourceFiles: 'build/*.zip'
						]]
				]]
			}
		}
	}
	post { 
		always {
			slackSend message: "Build UgCS video-transmitter ${version} - ${currentBuild.result}. (<${env.BUILD_URL}|Open>)"
		}
		success { notifyBuild('SUCCESSFUL') }
		failure { notifyBuild('FAILED') }
		aborted { notifyBuild('FAILED') }
	}
	options {
		buildDiscarder(logRotator(numToKeepStr:'10'))
		timeout(time: 30, unit: 'MINUTES')
	}
}

def notifyBuild(String buildStatus) {
	buildStatus =  buildStatus ?: 'SUCCESSFUL'

	def subject = "Build UgCS-CC video-transmitter ${version} - ${buildStatus}."
	def summary = "${subject} (${env.BUILD_URL})"
	def details = """
<html>
	<body>
		<article>
			<h3>Build UgCS-CC video-transmitter ${version} - ${buildStatus}.</h3>
		</article>
		<article>
			<h3>Summary</h3>
			<p>
				<table>
					<col width="60">
					<col width="300">
					<tr>
						<td>Git branch name</td>
						<td>
							<a href="https://bitbucket.org/sphengineering/ugcs-video-transmitter/branch/${git_branch_ugcs_video_transmitter}">${git_branch_ugcs_video_transmitter}</a>
						</td>
					</tr>
					<tr>
						<td>Git revision</td>
						<td>
							<a href="https://bitbucket.org/sphengineering/ugcs-video-transmitter.git/commits/${git_commit_ugcs_video_transmitter}">${git_commit_ugcs_video_transmitter}</a>
						</td>
					</tr>
					<tr>
						<td>Build logs</td>
						<td><a href="${env.BUILD_URL}">check build logs</a></td>
					</tr>
					<tr>
						<td>Synplicity path</td>
						<td>"Binaries/UgCS-CC/ugcs-video-transmitter/${version_short}"</td>
					</tr>
				</table>
			</p>
		</article>
		<article>
			<h3>Changelogs</h3>
			<p>${changeString}</p>
		</article>
	</body> 
</html>"""

	bitbucketStatusNotify(buildState: buildStatus)
	emailext(
		subject: subject, mimeType: 'text/html', body: details,
		to: 'ugcs_dev_cc@googlegroups.com'
	)
}


def getChangeString() {
	MAX_MSG_LEN = 100
	def changeString = ""
	echo "Gathering SCM changes"
	def changeLogSets = currentBuild.rawBuild.changeSets
	for (int i = 0; i < changeLogSets.size(); i++) {
		def entries = changeLogSets[i].items
		for (int j = 0; j < entries.length; j++) {
			def entry = entries[j]
			changeString += "<p><b>${entry.msg}</b></p>\n<p>${entry.author}: <a href='https://bitbucket.org/sphengineering/ugcs-video-transmitter.git/commits/${entry.commitId}'>${entry.commitId}</a></p>\n<p></p>\n"
		}
	}
	if (!changeString) {
		changeString = " No new changes"
	}
	return changeString
} 